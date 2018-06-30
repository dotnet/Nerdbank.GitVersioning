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

        Write-Host "Restoring Typings..." -ForegroundColor Yellow
        if ($PSCmdlet.ShouldProcess("$PSScriptRoot\src\nerdbank-gitversioning.npm", "typings install")) {
            .\node_modules\.bin\typings install
        }
    } finally {
        Pop-Location
    }

    Write-Host "Successfully restored all dependencies" -ForegroundColor Yellow
}
catch {
    Write-Error "Aborting script due to error"
    exit $lastexitcode
}
finally {
    $env:Platform=$oldPlatform
    Pop-Location
}
