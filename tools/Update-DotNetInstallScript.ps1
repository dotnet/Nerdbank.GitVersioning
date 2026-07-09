#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Updates the cached dotnet-install scripts from the dotnet/install-scripts GitHub repository.
.DESCRIPTION
    Downloads the latest dotnet-install.ps1 and dotnet-install.sh scripts from
    https://github.com/dotnet/install-scripts and caches them locally to avoid GitHub API rate limiting.
    Run this script periodically to get the latest installation scripts.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
Param()

$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$DownloadBaseUri = "https://raw.githubusercontent.com/dotnet/install-scripts/main/src"

$scripts = @('dotnet-install.ps1', 'dotnet-install.sh')

foreach ($script in $scripts) {
    $Uri = "$DownloadBaseUri/$script"
    $OutFile = Join-Path $ScriptRoot $script

    Write-Host "Updating $script from GitHub..."
    try {
        if ($PSCmdlet.ShouldProcess($OutFile, "Update from $Uri")) {
            Invoke-WebRequest -Uri $Uri -OutFile $OutFile -UseBasicParsing
            Write-Host "✓ Successfully updated $script" -ForegroundColor Green
        }
    }
    catch {
        Write-Error "Failed to update ${script}: $_"
        exit 1
    }
}

Write-Host "All cached scripts have been updated." -ForegroundColor Green
