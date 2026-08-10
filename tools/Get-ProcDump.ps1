<#
.SYNOPSIS
Downloads 32-bit and 64-bit procdump executables and returns the path to where they were installed.
#>
$version = '0.0.1'
$baseDir = "$PSScriptRoot\..\obj\tools"
$procDumpToolPath = "$baseDir\procdump\$version\bin"
if (-not (Test-Path $procDumpToolPath)) {
    & "$PSScriptRoot\Download-NuGetPackage.ps1" -PackageId procdump -Version $version -OutputDirectory $baseDir | Out-Null
}

(Resolve-Path $procDumpToolPath).Path
