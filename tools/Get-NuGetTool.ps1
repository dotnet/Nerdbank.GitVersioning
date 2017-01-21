<#
.SYNOPSIS
    Downloads the NuGet.exe tool and returns the path to it.
#>

function Expand-ZIPFile($file, $destination) {
    if (!(Test-Path $destination)) { $null = mkdir $destination }
    $shell = new-object -com shell.application
    $zip = $shell.NameSpace((Resolve-Path $file).Path)
    foreach ($item in $zip.items()) {
        $shell.Namespace((Resolve-Path $destination).Path).copyhere($item)
    }
}

$binaryToolsPath = "$PSScriptRoot\..\obj\tools"
if (!(Test-Path $binaryToolsPath)) { $null = mkdir $binaryToolsPath }
$nugetPath = "$binaryToolsPath\nuget.exe"
if (!(Test-Path $nugetPath)) {
    $NuGetVersion = "3.3.0"
    Write-Host "Downloading nuget.exe $NuGetVersion..." -ForegroundColor Yellow
    Invoke-WebRequest -Uri "https://dist.nuget.org/win-x86-commandline/v$NuGetVersion/NuGet.exe" -OutFile $nugetPath
}

$nugetPath
