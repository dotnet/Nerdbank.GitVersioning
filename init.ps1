<#
.SYNOPSIS
Restores all NuGet, NPM and Typings packages necessary to build this repository. 
#>
[CmdletBinding(SupportsShouldProcess)]
Param(
)

Push-Location $PSScriptRoot
try {
    $toolsPath = "$PSScriptRoot\tools"

    # First restore NuProj packages since the solution restore depends on NuProj evaluation succeeding.
    gci "$PSScriptRoot\src\project.json" -rec |? { $_.FullName -imatch 'nuget' } |% {
        & "$toolsPath\Restore-NuGetPackages.ps1" -Path $_ -Verbosity Quiet
    }

    # Restore VS solution dependencies
    gci "$PSScriptRoot\src" -rec |? { $_.FullName.EndsWith('.sln') } |% {
        & "$toolsPath\Restore-NuGetPackages.ps1" -Path $_.FullName -Verbosity Quiet
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

    Write-Host "Successfully restored all dependencies" -ForegroundColor Yellow
}
catch {
    Write-Error "Aborting script due to error"
    exit $lastexitcode
}
finally {
    Pop-Location
}
