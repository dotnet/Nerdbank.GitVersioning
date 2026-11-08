[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\bin\EcosystemPackages'),
    [string]$GoCommand = 'go',
    [string]$GradleCommand = 'gradle'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath, $repositoryRoot)
$rustManifest = Join-Path $repositoryRoot 'rust\nbgv\Cargo.toml'
$manifestText = Get-Content -Raw $rustManifest
if ($manifestText -notmatch '(?m)^version = "(?<version>[^"]+)"\r?$') {
    throw "Could not read the integration version from $rustManifest."
}

$integrationVersion = $Matches.version
$versionSources = @(
    @{
        Path = Join-Path $repositoryRoot 'rust\cargo-nbgv\Cargo.toml'
        Pattern = '(?m)^version = "(?<version>[^"]+)"\r?$'
    },
    @{
        Path = Join-Path $repositoryRoot 'java\build.gradle'
        Pattern = "getOrElse\('(?<version>[^']+)'\)"
    }
)
foreach ($versionSource in $versionSources) {
    $versionText = Get-Content -Raw $versionSource.Path
    if ($versionText -notmatch $versionSource.Pattern -or $Matches.version -ne $integrationVersion) {
        throw "$($versionSource.Path) does not use integration version $integrationVersion."
    }
}

function Invoke-Native {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,
        [Parameter(Mandatory)]
        [string[]]$ArgumentList,
        [string]$WorkingDirectory = $repositoryRoot
    )

    Push-Location $WorkingDirectory
    try {
        & $FilePath @ArgumentList
        if ($LASTEXITCODE -ne 0) {
            throw "$FilePath exited with code $LASTEXITCODE."
        }
    } finally {
        Pop-Location
    }
}

if (Test-Path $OutputPath) {
    Remove-Item -Recurse -Force $OutputPath
}

$rustOutput = New-Item -ItemType Directory -Force (Join-Path $OutputPath 'rust')
$goOutput = New-Item -ItemType Directory -Force (Join-Path $OutputPath 'go')
$javaOutput = New-Item -ItemType Directory -Force (Join-Path $OutputPath 'java')

$parityScript = Join-Path $repositoryRoot 'test\Nerdbank.GitVersioning.Tests\AdapterParity.ps1'
& $parityScript -Configuration $Configuration -GoCommand $GoCommand -GradleCommand $GradleCommand
if (-not $?) {
    throw "$parityScript failed."
}

foreach ($crate in @('nbgv', 'cargo-nbgv')) {
    $manifest = Join-Path $repositoryRoot "rust\$crate\Cargo.toml"
    Invoke-Native cargo @(
        'package',
        '--manifest-path', $manifest,
        '--locked',
        '--allow-dirty'
    )

    $cratePath = Join-Path $repositoryRoot "rust\target\package\$crate-$integrationVersion.crate"
    if (-not (Test-Path $cratePath)) {
        throw "Cargo did not produce $cratePath."
    }

    Copy-Item $cratePath $rustOutput.FullName
}

$goTool = (Get-Command $GoCommand -ErrorAction Stop).Source
Invoke-Native $goTool @('mod', 'verify') (Join-Path $repositoryRoot 'go')
$originalGoOs = $env:GOOS
$originalGoArch = $env:GOARCH
$originalCgoEnabled = $env:CGO_ENABLED
try {
    $env:CGO_ENABLED = '0'
    foreach ($target in @(
        @{ OS = 'linux'; Arch = 'amd64' },
        @{ OS = 'linux'; Arch = 'arm64' },
        @{ OS = 'windows'; Arch = 'amd64' },
        @{ OS = 'windows'; Arch = 'arm64' },
        @{ OS = 'darwin'; Arch = 'amd64' },
        @{ OS = 'darwin'; Arch = 'arm64' }
    )) {
        $env:GOOS = $target.OS
        $env:GOARCH = $target.Arch
        $suffix = if ($target.OS -eq 'windows') { '.exe' } else { '' }
        $outputFile = Join-Path $goOutput.FullName "nbgv-go-$integrationVersion-$($target.OS)-$($target.Arch)$suffix"
        Invoke-Native $goTool @(
            'build',
            '-trimpath',
            '-buildvcs=false',
            '-o', $outputFile,
            './cmd/nbgv-go'
        ) (Join-Path $repositoryRoot 'go')
    }
} finally {
    $env:GOOS = $originalGoOs
    $env:GOARCH = $originalGoArch
    $env:CGO_ENABLED = $originalCgoEnabled
}

Copy-Item (Join-Path $repositoryRoot 'go\README.md') (Join-Path $goOutput.FullName 'nbgv-go-README.md')
Copy-Item (Join-Path $repositoryRoot 'LICENSE') (Join-Path $goOutput.FullName 'nbgv-go-LICENSE.txt')

$gradleTool = (Get-Command $GradleCommand -ErrorAction Stop).Source
Invoke-Native $gradleTool @(
    'clean',
    'test',
    ':gradle-plugin:validatePlugins',
    ':adapter-core:publishAllPublicationsToStagingRepository',
    ':maven-extension:publishAllPublicationsToStagingRepository',
    ':gradle-plugin:publishAllPublicationsToStagingRepository',
    '--no-daemon',
    "--console=plain",
    "-PintegrationVersion=$integrationVersion"
) (Join-Path $repositoryRoot 'java')

$javaRepository = Join-Path $repositoryRoot 'java\build\maven-repository'
$expectedJavaArtifacts = @(
    "nbgv-adapter-$integrationVersion.jar",
    "nbgv-adapter-$integrationVersion-sources.jar",
    "nbgv-adapter-$integrationVersion-javadoc.jar",
    "nbgv-adapter-$integrationVersion.pom",
    "nbgv-maven-extension-$integrationVersion.jar",
    "nbgv-maven-extension-$integrationVersion-sources.jar",
    "nbgv-maven-extension-$integrationVersion-javadoc.jar",
    "nbgv-maven-extension-$integrationVersion.pom",
    "nbgv-gradle-plugin-$integrationVersion.jar",
    "nbgv-gradle-plugin-$integrationVersion-sources.jar",
    "nbgv-gradle-plugin-$integrationVersion-javadoc.jar",
    "nbgv-gradle-plugin-$integrationVersion.pom",
    "com.microsoft.nerdbank-gitversioning.gradle.plugin-$integrationVersion.pom"
)
foreach ($fileName in $expectedJavaArtifacts) {
    if (-not (Get-ChildItem -Path $javaRepository -Filter $fileName -File -Recurse)) {
        throw "The staged Maven repository is missing $fileName."
    }
}

$javaArchive = Join-Path $javaOutput.FullName "nbgv-java-maven-repository-$integrationVersion.zip"
Compress-Archive -Path (Join-Path $javaRepository '*') -DestinationPath $javaArchive
Copy-Item (Join-Path $repositoryRoot 'java\README.md') (Join-Path $javaOutput.FullName 'nbgv-java-README.md')
Copy-Item (Join-Path $repositoryRoot 'LICENSE') (Join-Path $javaOutput.FullName 'nbgv-java-LICENSE.txt')

@{
    version = $integrationVersion
    rustCrates = @('nbgv', 'cargo-nbgv')
    goModule = 'github.com/dotnet/Nerdbank.GitVersioning/go'
    mavenGroup = 'com.microsoft.nerdbank-gitversioning'
    mavenArtifacts = @('nbgv-adapter', 'nbgv-maven-extension', 'nbgv-gradle-plugin')
    gradlePlugin = 'com.microsoft.nerdbank-gitversioning'
} | ConvertTo-Json | Set-Content -Encoding utf8NoBOM (Join-Path $OutputPath 'packages.json')

$packageFiles = Get-ChildItem $OutputPath -Recurse -File
$duplicateNames = $packageFiles | Group-Object Name | Where-Object Count -gt 1
if ($duplicateNames) {
    throw "Release asset names must be unique: $($duplicateNames.Name -join ', ')."
}

$checksums = $packageFiles |
    Sort-Object Name |
    ForEach-Object {
        $hash = (Get-FileHash -Algorithm SHA256 $_.FullName).Hash.ToLowerInvariant()
        "$hash *$($_.Name)"
    }
$checksums | Set-Content -Encoding ascii (Join-Path $OutputPath 'ecosystem-SHA256SUMS.txt')

Write-Host "Ecosystem packages were written to $OutputPath."
