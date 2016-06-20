<#
.SYNOPSIS
Restores all NuGet, NPM and Typings packages necessary to build this repository. 
#>
[CmdletBinding(SupportsShouldProcess)]
Param(
)

Write-Host "Restoring NuGet packages..." -ForegroundColor Yellow
if ($PSCmdlet.ShouldProcess("$PSScriptRoot\src", "nuget restore")) {
    nuget restore "$PSScriptRoot\src" -Verbosity quiet
}

Write-Host "Restoring NPM packages..." -ForegroundColor Yellow
Push-Location "$PSScriptRoot\src\nerdbank-gitversioning.npm"
if ($PSCmdlet.ShouldProcess("$PSScriptRoot\src\nerdbank-gitversioning.npm", "npm install")) {
    npm install --loglevel error
}

Write-Host "Restoring Typings..." -ForegroundColor Yellow
if ($PSCmdlet.ShouldProcess("$PSScriptRoot\src\nerdbank-gitversioning.npm", "typings install")) {
    .\node_modules\.bin\typings install
}

Pop-Location
