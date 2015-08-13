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
    [Parameter(Mandatory=$true)]
    [string]$Version
)

$DependencyBasePath = "$PSScriptRoot\..\build"
[Reflection.Assembly]::LoadFile((Resolve-Path "$DependencyBasePath\Validation.dll"))
[Reflection.Assembly]::LoadFile((Resolve-Path "$DependencyBasePath\NerdBank.GitVersioning.dll"))
[Reflection.Assembly]::LoadFile((Resolve-Path "$DependencyBasePath\LibGit2Sharp.dll"))

$ProjectDirectory = Resolve-Path $ProjectDirectory
$GitPath = $ProjectDirectory
while (!(Test-Path "$GitPath\.git")) {
    $GitPath = Split-Path $GitPath
}

$RepoRelativeProjectDirectory = $ProjectDirectory.Substring($GitPath.Length)

$TypedVersion = New-Object Version($Version)

$repo = New-Object LibGit2Sharp.Repository($GitPath)
try {
    $commit = [NerdBank.GitVersioning.GitExtensions]::GetCommitFromVersion($repo, $TypedVersion, $RepoRelativeProjectDirectory)
    $commit.Id.Sha
} finally {
    $repo.Dispose()
}
