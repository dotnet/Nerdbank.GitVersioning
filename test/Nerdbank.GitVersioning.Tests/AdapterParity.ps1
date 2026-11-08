[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Framework = 'net8.0',
    [string]$GoCommand = 'go',
    [string]$GradleCommand = 'gradle',
    [switch]$PrepareOnly
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$fixtureRoot = Join-Path $repositoryRoot 'obj\AdapterParity'
$fixtureRepository = Join-Path $fixtureRoot 'repository'
$fixtureWorktree = Join-Path $fixtureRoot 'worktree'
$fixtureShallow = Join-Path $fixtureRoot 'shallow'
$fixtureDirty = Join-Path $fixtureRoot 'dirty'
$manifestPath = Join-Path $fixtureRoot 'manifest.json'
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)

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

function Write-Utf8File {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [string]$Content
    )

    $parent = Split-Path -Parent $Path
    if ($parent) {
        [System.IO.Directory]::CreateDirectory($parent) | Out-Null
    }

    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

function Add-FixtureCommit {
    param(
        [Parameter(Mandatory)]
        [string]$Message,
        [Parameter(Mandatory)]
        [string]$Timestamp
    )

    $previousAuthorDate = $env:GIT_AUTHOR_DATE
    $previousCommitterDate = $env:GIT_COMMITTER_DATE
    try {
        $env:GIT_AUTHOR_DATE = $Timestamp
        $env:GIT_COMMITTER_DATE = $Timestamp
        Invoke-Native git @('-C', $fixtureRepository, 'add', '.')
        Invoke-Native git @('-C', $fixtureRepository, 'commit', '--quiet', '-m', $Message)
    } finally {
        $env:GIT_AUTHOR_DATE = $previousAuthorDate
        $env:GIT_COMMITTER_DATE = $previousCommitterDate
    }
}

function Write-Oracle {
    param(
        [Parameter(Mandatory)]
        [string]$Name,
        [Parameter(Mandatory)]
        [string]$Directory,
        [Parameter(Mandatory)]
        [string]$GitEngine
    )

    $previousGitEngine = $env:NBGV_GitEngine
    try {
        $env:NBGV_GitEngine = $GitEngine
        $lines = & $script:nbgvExecutable get-version --format adapter --project $Directory
        if ($LASTEXITCODE -ne 0) {
            throw "nbgv failed to create the '$Name' oracle with exit code $LASTEXITCODE."
        }

        $json = ($lines -join "`n") + "`n"
        $oraclePath = Join-Path $fixtureRoot "$Name.json"
        Write-Utf8File $oraclePath $json
        $document = $json | ConvertFrom-Json
        if ($GitEngine -in @('Managed', 'Disabled')) {
            $nativeLines = & $script:nativeNbgvExecutable get-version --format adapter --project $Directory
            if ($LASTEXITCODE -ne 0) {
                throw "Native nbgv failed to create the '$Name' oracle with exit code $LASTEXITCODE."
            }

            $nativeDocument = ($nativeLines -join "`n") | ConvertFrom-Json
            foreach ($field in @('schemaVersion', 'semVer2', 'version', 'versionHeight', 'gitCommitId', 'gitCommitIdShort', 'gitCommitDate', 'publicRelease')) {
                if ($document.$field -cne $nativeDocument.$field) {
                    throw "Native and .NET adapter output differ for '$Name' field '$field': $($nativeDocument.$field) vs $($document.$field)."
                }
            }
        }

        return [pscustomobject]@{
            name = $Name
            directory = $Directory
            oracle = $oraclePath
            gitEngine = $GitEngine
            document = $document
        }
    } finally {
        $env:NBGV_GitEngine = $previousGitEngine
    }
}

Push-Location $repositoryRoot
try {
    & '.\.github\Prime-ForCopilot.ps1'
    $previousGitEngine = $env:NBGV_GitEngine
    try {
        $env:NBGV_GitEngine = 'Disabled'
        Invoke-Native dotnet @(
            'build',
            'src\nbgv\nbgv.csproj',
            '--configuration', $Configuration,
            '--framework', $Framework,
            '--no-restore',
            '--nologo'
        )
    } finally {
        $env:NBGV_GitEngine = $previousGitEngine
    }

    $nbgvFileName = if ($IsWindows) { 'nbgv.exe' } else { 'nbgv' }
    $script:nbgvExecutable = Join-Path $repositoryRoot "bin\nbgv\$Configuration\$Framework\$nbgvFileName"
    if (-not (Test-Path $script:nbgvExecutable)) {
        throw "Built nbgv executable was not found at $script:nbgvExecutable."
    }

    $rustWorkspace = Join-Path $repositoryRoot 'src\nerdbank-gitversioning-rs'
    $rustTarget = Join-Path $rustWorkspace 'target'
    Invoke-Native cargo @('build', '--manifest-path', (Join-Path $rustWorkspace 'Cargo.toml'), '--target-dir', $rustTarget, '-p', 'nbgv')
    $script:nativeNbgvExecutable = Join-Path $rustTarget "debug\$nbgvFileName"
    if (-not (Test-Path $script:nativeNbgvExecutable)) {
        throw "Built native nbgv executable was not found at $script:nativeNbgvExecutable."
    }

    if (Test-Path $fixtureRoot) {
        Remove-Item -Recurse -Force $fixtureRoot
    }

    [System.IO.Directory]::CreateDirectory($fixtureRepository) | Out-Null
    Invoke-Native git @('-C', $fixtureRepository, 'init', '--quiet', '-b', 'main')
    Invoke-Native git @('-C', $fixtureRepository, 'config', 'user.name', 'Adapter Parity')
    Invoke-Native git @('-C', $fixtureRepository, 'config', 'user.email', 'adapter-parity@example.com')
    Invoke-Native git @('-C', $fixtureRepository, 'config', 'core.autocrlf', 'false')

    Write-Utf8File (Join-Path $fixtureRepository 'version.json') (
        '{"version":"3.4-beta","publicReleaseRefSpec":["^refs/heads/main$"],"pathFilters":["src"]}'
    )
    Write-Utf8File (Join-Path $fixtureRepository 'src\tracked.txt') "one`n"
    Add-FixtureCommit 'Add prerelease version and tracked source' '2026-01-02T03:04:05Z'

    Write-Utf8File (Join-Path $fixtureRepository 'docs\ignored.txt') "does not affect height`n"
    Add-FixtureCommit 'Change a path excluded from version height' '2026-01-03T03:04:05Z'

    Write-Utf8File (Join-Path $fixtureRepository 'src\tracked.txt') "two`n"
    Add-FixtureCommit 'Change a path included in version height' '2026-01-04T03:04:05Z'

    Write-Utf8File (Join-Path $fixtureRepository 'child\version.json') '{"inherit":true}'
    Add-FixtureCommit 'Add inherited child version' '2026-01-05T03:04:05Z'

    Invoke-Native git @('-C', $fixtureRepository, 'branch', 'feature')
    Invoke-Native git @('-C', $fixtureRepository, 'worktree', 'add', '--quiet', $fixtureWorktree, 'feature')
    Invoke-Native git @(
        'clone',
        '--quiet',
        '--depth', '4',
        '--no-local',
        '--branch', 'main',
        $fixtureRepository,
        $fixtureShallow
    )

    $normal = Write-Oracle 'public-prerelease-path-filtered' $fixtureRepository 'Managed'
    $inherited = Write-Oracle 'inherited-version' (Join-Path $fixtureRepository 'child') 'Managed'
    $worktree = Write-Oracle 'non-public-worktree' $fixtureWorktree 'Managed'
    $shallow = Write-Oracle 'shallow-clone' $fixtureShallow 'Managed'
    $disabled = Write-Oracle 'disabled-no-git-data' $fixtureRepository 'Disabled'

    Invoke-Native git @('clone', '--quiet', $fixtureRepository, $fixtureDirty)
    Invoke-Native git @('-C', $fixtureDirty, 'config', 'user.name', 'Adapter Parity')
    Invoke-Native git @('-C', $fixtureDirty, 'config', 'user.email', 'adapter-parity@example.com')
    Write-Utf8File (Join-Path $fixtureDirty 'version.json') (
        '{"version":"3.4-beta","gitCommitIdIncludeDirty":true,"publicReleaseRefSpec":["^refs/heads/main$"],"pathFilters":["src"]}'
    )
    Invoke-Native git @('-C', $fixtureDirty, 'add', 'version.json')
    Invoke-Native git @('-C', $fixtureDirty, 'commit', '--quiet', '-m', 'Enable dirty commit metadata')
    Write-Utf8File (Join-Path $fixtureDirty 'src\tracked.txt') "dirty source`n"
    $dirty = Write-Oracle 'dirty-working-tree' $fixtureDirty 'Managed'
    if ($dirty.document.gitCommitId -notmatch '-dirty$' -or $dirty.document.gitCommitIdShort -notmatch '-dirty$') {
        throw 'The dirty working tree fixture did not retain dirty commit metadata.'
    }

    if (-not $normal.document.publicRelease -or $normal.document.semVer2 -notmatch '-beta') {
        throw 'The public prerelease fixture did not produce the intended version.'
    }

    if ($normal.document.versionHeight -ne 2) {
        throw "Path-filtered history should have height 2, not $($normal.document.versionHeight)."
    }

    if ($inherited.document.semVer2 -ne $normal.document.semVer2) {
        throw 'The inherited version fixture diverged from its parent.'
    }

    if ($worktree.document.publicRelease -or $worktree.document.semVer2 -notmatch '\.g[0-9a-f]+') {
        throw 'The feature worktree did not produce the intended non-public version.'
    }

    $isShallow = (& git -C $fixtureShallow rev-parse --is-shallow-repository).Trim()
    if ($LASTEXITCODE -ne 0 -or $isShallow -ne 'true') {
        throw 'The shallow clone fixture is not shallow.'
    }

    if ($shallow.document.semVer2 -ne $normal.document.semVer2) {
        throw 'The sufficiently deep shallow clone did not match the complete repository.'
    }

    if ($null -ne $disabled.document.gitCommitId -or
        $null -ne $disabled.document.gitCommitIdShort -or
        $null -ne $disabled.document.gitCommitDate) {
        throw 'The disabled Git fixture should have null Git fields.'
    }

    $scenarios = @($normal, $inherited, $worktree, $shallow, $disabled, $dirty) | ForEach-Object {
        [pscustomobject]@{
            name = $_.name
            directory = $_.directory
            oracle = $_.oracle
            gitEngine = $_.gitEngine
        }
    }
    $manifest = [pscustomobject]@{
        executable = $script:nativeNbgvExecutable
        dotnetExecutable = $script:nbgvExecutable
        scenarios = $scenarios
    }
    Write-Utf8File $manifestPath (($manifest | ConvertTo-Json -Depth 4) + "`n")

    Write-Host "Adapter parity fixture: $manifestPath"
    if ($PrepareOnly) {
        return
    }

    $env:NBGV_PARITY_MANIFEST = $manifestPath
    $env:NBGV_EXECUTABLE = $script:nativeNbgvExecutable
    $env:NBGV_GitEngine = 'Managed'
    $env:PublicRelease = $null

    $previousGitEngine = $env:NBGV_GitEngine
    try {
        $env:NBGV_GitEngine = 'Disabled'
        Invoke-Native dotnet @(
            'build',
            'test\Nerdbank.GitVersioning.Tests\Nerdbank.GitVersioning.Tests.csproj',
            '--configuration', $Configuration,
            '--framework', 'net10.0',
            '--no-restore',
            '--nologo'
        )
    } finally {
        $env:NBGV_GitEngine = $previousGitEngine
    }
    Invoke-Native dotnet @(
        'test',
        '--project', 'test\Nerdbank.GitVersioning.Tests\Nerdbank.GitVersioning.Tests.csproj',
        '--configuration', $Configuration,
        '--framework', 'net10.0',
        '--no-build',
        '--',
        '--treenode-filter', '/*/*/AdapterVersionInfoTests/*'
    )
    Invoke-Native cargo @('test', '--manifest-path', 'src\nerdbank-gitversioning-rs\Cargo.toml', '--workspace')

    $goTool = Get-Command $GoCommand -ErrorAction SilentlyContinue
    if (-not $goTool) {
        throw 'Go 1.22 or newer is required to run adapter parity tests.'
    }
    Invoke-Native $goTool.Source @('test', './...') (Join-Path $repositoryRoot 'go')

    $gradleTool = Get-Command $GradleCommand -ErrorAction SilentlyContinue
    if (-not $gradleTool) {
        throw 'Gradle 8.8 or newer is required to run adapter parity tests.'
    }
    Invoke-Native $gradleTool.Source @('test', '--no-daemon') (Join-Path $repositoryRoot 'java')
} finally {
    Pop-Location
}
