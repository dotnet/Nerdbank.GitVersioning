<#
.SYNOPSIS
    Restores NuGet packages.
.PARAMETER Path
    The path of the solution, directory, packages.config or project.json file to restore packages from.
    If not specified, the current directory is used.
.PARAMETER Verbosity
#>
Param(
    [Parameter(Position=1)]
    [string]$Path=(Get-Location),
    [Parameter()]
    [ValidateSet('Quiet','Normal','Detailed')]
    [string]$Verbosity='normal'
)

$nugetPath = & "$PSScriptRoot\Get-NuGetTool.ps1"

Write-Verbose "Restoring NuGet packages for $Path"
& $nugetPath restore $Path -Verbosity $Verbosity
if ($lastexitcode -ne 0) { throw }
