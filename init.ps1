<#
.SYNOPSIS
Restores all NuGet, NPM and Typings packages necessary to build this repository.
#>
[CmdletBinding(SupportsShouldProcess)]
Param(
)

$oldPlatform=$env:Platform
$env:Platform='Any CPU' # Some people wander in here from a platform-specific build window.

Push-Location $PSScriptRoot
try {
    msbuild "$PSScriptRoot\src" /t:restore /v:minimal /m /nologo

    Write-Host "Restoring NPM packages..." -ForegroundColor Yellow
    Push-Location "$PSScriptRoot\src\nerdbank-gitversioning.npm"
    try {
        if ($PSCmdlet.ShouldProcess("$PSScriptRoot\src\nerdbank-gitversioning.npm", "npm install")) {
            npm install --loglevel error
        }
    } finally {
        Pop-Location
    }

    Write-Host "Successfully restored all dependencies" -ForegroundColor Yellow
} catch {
    # we have the try so that PS fails when we get failure exit codes from build steps.
    throw;
} finally {
    $env:Platform=$oldPlatform
    Pop-Location
}
