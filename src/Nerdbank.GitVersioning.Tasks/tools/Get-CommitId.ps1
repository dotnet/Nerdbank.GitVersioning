<#
.SYNOPSIS
Finds the git commit ID that was built to produce some specific version of an assembly.
.DESCRIPTION
TODO
.PARAMETER GitPath
The path to the git repo root. If omitted, the current working directory is assumed.
.PARAMETER AssemblyPath
Path to the assembly to read the version from.
.PARAMETER Version
The major.minor.build.revision version string or semver-compliant version read from the assembly.
.PARAMETER ProjectDirectory
The directory of the project that built the assembly, within the git repo.
#>
[CmdletBinding(SupportsShouldProcess)]
Param(
    [Parameter()]
    [string]$ProjectDirectory=".",
    [Parameter()]
    [string]$AssemblyPath,
    [Parameter(Mandatory=$true, Position=0)]
    [string]$Version
)

if (-not (Test-Path variable:global:DependencyBasePath) -or !$DependencyBasePath) { $DependencyBasePath = "$PSScriptRoot\..\build\MSBuildFull" }
$null = [Reflection.Assembly]::LoadFile((Resolve-Path "$DependencyBasePath\Validation.dll"))
$null = [Reflection.Assembly]::LoadFile((Resolve-Path "$DependencyBasePath\NerdBank.GitVersioning.dll"))
$null = [Reflection.Assembly]::LoadFile((Resolve-Path "$DependencyBasePath\LibGit2Sharp.dll"))
$null = [Reflection.Assembly]::LoadFile((Resolve-Path "$DependencyBasePath\Newtonsoft.Json.dll"))
[Nerdbank.GitVersioning.GitExtensions]::HelpFindLibGit2NativeBinaries($DependencyBasePath)

$ProjectDirectory = (Resolve-Path $ProjectDirectory).ProviderPath
$GitPath = $ProjectDirectory
while (!(Test-Path "$GitPath\.git") -and $GitPath.Length -gt 0) {
    $GitPath = Split-Path $GitPath
}

if ($GitPath -eq '') {
    Write-Error "Unable to find git repo in $ProjectDirectory."
    return 1
}

$RepoRelativeProjectDirectory = $ProjectDirectory.Substring($GitPath.Length).TrimStart([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)

$TypedVersion = New-Object Version($Version)

$repo = New-Object LibGit2Sharp.Repository($GitPath)
try {
    $commit = [NerdBank.GitVersioning.GitExtensions]::GetCommitsFromVersion($repo, $TypedVersion, $RepoRelativeProjectDirectory)
    $commit.Id.Sha
} finally {
    $repo.Dispose()
}
