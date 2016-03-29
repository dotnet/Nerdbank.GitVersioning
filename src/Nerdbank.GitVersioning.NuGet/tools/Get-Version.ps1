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
    [string]$ProjectDirectory="."
)

if (!$DependencyBasePath) { $DependencyBasePath = "$PSScriptRoot\..\build" }
$null = [Reflection.Assembly]::LoadFile((Resolve-Path "$DependencyBasePath\Validation.dll"))
$null = [Reflection.Assembly]::LoadFile((Resolve-Path "$DependencyBasePath\NerdBank.GitVersioning.dll"))
$null = [Reflection.Assembly]::LoadFile((Resolve-Path "$DependencyBasePath\LibGit2Sharp.dll"))
$null = [Reflection.Assembly]::LoadFile((Resolve-Path "$DependencyBasePath\Newtonsoft.Json.dll"))

$ProjectDirectory = Resolve-Path $ProjectDirectory
$GitPath = $ProjectDirectory
while (!(Test-Path "$GitPath\.git") -and $GitPath.Length -gt 0) {
    $GitPath = Split-Path $GitPath
}

if ($GitPath -eq '') {
    Write-Error "Unable to find git repo in $ProjectDirectory."
    return 1
}

$RepoRelativeProjectDirectory = $ProjectDirectory.Substring($GitPath.Length).TrimStart([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)

$repo = New-Object LibGit2Sharp.Repository($GitPath)
try {
    $Head = [System.Linq.Enumerable]::First($repo.Head.Commits)
    $versionOptions = [NerdBank.GitVersioning.VersionFile]::GetVersion($repo, $RepoRelativeProjectDirectory)
    $version = [NerdBank.GitVersioning.GitExtensions]::GetIdAsVersion($repo, $RepoRelativeProjectDirectory)
    $SimpleVersion = New-Object Version ($version.Major, $version.Minor, $version.Build)
    $MajorMinorVersion = New-Object Version ($version.Major, $version.Minor)
    $PrereleaseVersion = $versionOptions.Version.Prerelease
    $SemVer1 = "{0}{1}-g{2}" -f $SimpleVersion, $PrereleaseVersion, $Head.Id.Sha.Substring(0,10)
    $SemVer2 = "{0}{1}+g{2}" -f $SimpleVersion, $PrereleaseVersion, $Head.Id.Sha.Substring(0,10)
    $RichObject = New-Object PSObject
    Add-Member -InputObject $RichObject -MemberType NoteProperty -Name Version -Value $version
    Add-Member -InputObject $RichObject -MemberType NoteProperty -Name SimpleVersion -Value $SimpleVersion
    Add-Member -InputObject $RichObject -MemberType NoteProperty -Name MajorMinorVersion  -Value $MajorMinorVersion
    Add-Member -InputObject $RichObject -MemberType NoteProperty -Name PrereleaseVersion -Value $PrereleaseVersion
    Add-Member -InputObject $RichObject -MemberType NoteProperty -Name CommitId -Value $Head.Id.Sha
    Add-Member -InputObject $RichObject -MemberType NoteProperty -Name CommitIdShort -Value $Head.Id.Sha.Substring(0, 10)
    Add-Member -InputObject $RichObject -MemberType NoteProperty -Name VersionHeight -Value $version.Build
    Add-Member -InputObject $RichObject -MemberType NoteProperty -Name SemVer1 -Value $SemVer1
    Add-Member -InputObject $RichObject -MemberType NoteProperty -Name SemVer2 -Value $SemVer2
    $RichObject
} finally {
    $repo.Dispose()
}
