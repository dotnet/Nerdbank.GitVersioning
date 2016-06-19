<#
.SYNOPSIS
Finds the git commit ID that was built to produce some specific version of an assembly.
.PARAMETER ProjectDirectory
The directory of the project that built the assembly, within the git repo.
.PARAMETER PublicRelease
Forces version generation to consider this a public release. 
Similar to the MSBuild /p:PublicRelease=true switch.
#>
[CmdletBinding(SupportsShouldProcess)]
Param(
    [Parameter()]
    [string]$ProjectDirectory=".",
    [Parameter()]
    [switch]$PublicRelease
)

if (!$DependencyBasePath) { $DependencyBasePath = "$PSScriptRoot\..\build" }
$null = [Reflection.Assembly]::LoadFile((Resolve-Path "$DependencyBasePath\Validation.dll"))
$null = [Reflection.Assembly]::LoadFile((Resolve-Path "$DependencyBasePath\NerdBank.GitVersioning.dll"))
$null = [Reflection.Assembly]::LoadFile((Resolve-Path "$DependencyBasePath\LibGit2Sharp.dll"))
$null = [Reflection.Assembly]::LoadFile((Resolve-Path "$DependencyBasePath\Newtonsoft.Json.dll"))

$ProjectDirectory = Resolve-Path $ProjectDirectory

try {
    $CloudBuild = [Nerdbank.GitVersioning.CloudBuild]::Active
    $VersionOracle = [Nerdbank.GitVersioning.VersionOracle]::Create($ProjectDirectory, $null, $CloudBuild)
    $VersionOracle.PublicRelease = $PublicRelease

    $VersionOracle
}
finally {
    # the try is here so Powershell aborts after first failed step.
}