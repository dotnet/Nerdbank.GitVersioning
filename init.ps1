#!/usr/bin/env pwsh

<#
.SYNOPSIS
Installs dependencies required to build and test the projects in this repository.
.DESCRIPTION
This MAY not require elevation, as the SDK and runtimes are installed to a per-user location,
unless the `-InstallLocality` switch is specified directing to a per-repo or per-machine location.
See detailed help on that switch for more information.
.PARAMETER InstallLocality
A value indicating whether dependencies should be installed locally to the repo or at a per-user location.
Per-user allows sharing the installed dependencies across repositories and allows use of a shared expanded package cache.
Visual Studio will only notice and use these SDKs/runtimes if VS is launched from the environment that runs this script.
Per-repo allows for high isolation, allowing for a more precise recreation of the environment within an Azure Pipelines build.
When using 'repo', environment variables are set to cause the locally installed dotnet SDK to be used.
Per-repo can lead to file locking issues when dotnet.exe is left running as a build server and can be mitigated by running `dotnet build-server shutdown`.
Per-machine requires elevation and will download and install all SDKs and runtimes to machine-wide locations so all applications can find it.
.PARAMETER NoPrerequisites
Skips the installation of prerequisite software (e.g. SDKs, tools).
.PARAMETER NoRestore
Skips the package restore step.
#>
[CmdletBinding(SupportsShouldProcess=$true)]
Param(
    [ValidateSet('repo','user','machine')]
    [string]$InstallLocality='user',
    [Parameter()]
    [switch]$NoPrerequisites,
    [Parameter()]
    [switch]$NoRestore
)

if (!$NoPrerequisites) {
    & "$PSScriptRoot\tools\Install-DotNetSdk.ps1" -InstallLocality $InstallLocality
}

$oldPlatform=$env:Platform
$env:Platform='Any CPU' # Some people wander in here from a platform-specific build window.

Push-Location $PSScriptRoot
try {
    $HeaderColor = 'Green'

    if (!$NoRestore -and $PSCmdlet.ShouldProcess("NuGet packages", "Restore")) {
        Write-Host "Restoring NuGet packages" -ForegroundColor $HeaderColor
        dotnet restore "$PSScriptRoot\src"
        if ($lastexitcode -ne 0) {
            throw "Failure while restoring packages."
        }
    }

    Write-Host "Restoring NPM packages..." -ForegroundColor Yellow
    Push-Location "$PSScriptRoot\src\nerdbank-gitversioning.npm"
    try {
        if ($PSCmdlet.ShouldProcess("$PSScriptRoot\src\nerdbank-gitversioning.npm", "yarn install")) {
            yarn install --loglevel error
        }
    } finally {
        Pop-Location
    }

    Write-Host "Successfully restored all dependencies" -ForegroundColor Yellow
} catch {
    Write-Error $error[0]
    exit $lastexitcode
} finally {
    $env:Platform=$oldPlatform
    Pop-Location
}
