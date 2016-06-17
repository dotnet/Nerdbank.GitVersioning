Write-Host "Restoring NuGet packages..." -ForegroundColor Yellow
nuget restore "$PSScriptRoot\src" -Verbosity quiet

Write-Host "Restoring NPM packages..." -ForegroundColor Yellow
Push-Location "$PSScriptRoot\src\nerdbank-gitversioning.npm"
npm install --loglevel error

Write-Host "Restoring Typings..." -ForegroundColor Yellow
 .\node_modules\.bin\typings install
Pop-Location
