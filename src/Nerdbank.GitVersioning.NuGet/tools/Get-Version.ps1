<#
.SYNOPSIS
Finds the git commit ID that was built to produce some specific version of an assembly.
.PARAMETER ProjectDirectory
The directory of the project that built the assembly, within the git repo.
#>
[CmdletBinding(SupportsShouldProcess)]
Param(
    [Parameter()]
    [string]$ProjectDirectory="."
)

if (-not (Test-Path variable:global:DependencyBasePath) -or !$DependencyBasePath) { $DependencyBasePath = "$PSScriptRoot\..\build\MSBuildFull" }
$null = [Reflection.Assembly]::LoadFile((Resolve-Path "$DependencyBasePath\Validation.dll"))
$null = [Reflection.Assembly]::LoadFile((Resolve-Path "$DependencyBasePath\NerdBank.GitVersioning.dll"))
$null = [Reflection.Assembly]::LoadFile((Resolve-Path "$DependencyBasePath\LibGit2Sharp.dll"))
$null = [Reflection.Assembly]::LoadFile((Resolve-Path "$DependencyBasePath\Newtonsoft.Json.dll"))

$ProjectDirectory = (Resolve-Path $ProjectDirectory).ProviderPath

try {
    [Nerdbank.GitVersioning.GitExtensions]::HelpFindLibGit2NativeBinaries($DependencyBasePath)
    $CloudBuild = [Nerdbank.GitVersioning.CloudBuild]::Active
    $VersionOracle = [Nerdbank.GitVersioning.VersionOracle]::Create($ProjectDirectory, $null, $CloudBuild)
    $VersionOracle
}
finally {
    # the try is here so Powershell aborts after first failed step.
}
