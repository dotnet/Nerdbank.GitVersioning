#!/usr/bin/env pwsh

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

Push-Location (Join-Path $PSScriptRoot '..')
try {
    dotnet build-server shutdown
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    git clean -fdx
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    git config user.name "test user"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    git config user.email "andrewarnott@gmail.com"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    if ($IsLinux) {
        Write-Host "##[group]strong-name-tool installation"
        Write-Host "##[command]sudo apt-get install strong-name-tool"
        sudo apt-get install strong-name-tool 2>&1
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        Write-Host "##[endgroup]"
    }

    if ($IsMacOS) {
        Write-Host "##[group]mono installation"
        Write-Host "##[command]brew install mono"
        brew install mono 2>&1
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        Write-Host "##[endgroup]"
    }

    ./Expand-Template.ps1 -LibraryName Calc -Author "Andrew Arnott"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    dotnet build
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
} finally {
    Pop-Location
}
