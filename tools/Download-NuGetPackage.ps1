<#
.SYNOPSIS
    Downloads a NuGet package to a local folder using dotnet package download.
.PARAMETER PackageId
    The Package ID to download.
.PARAMETER Version
    The version of the package to download. If unspecified, the latest version is downloaded.
.PARAMETER Source
    An additional package source to search. Used as a fallback alongside the configured feeds.
.PARAMETER OutputDirectory
    The directory to download the package to. By default, it uses the obj\tools folder at the root of the repo.
.PARAMETER ConfigFile
    The nuget.config file to use. By default, it uses the repo root nuget.config.
.PARAMETER Verbosity
    The verbosity level for the download. Defaults to quiet.
.OUTPUTS
    System.String. The path to the downloaded package directory.
#>
[CmdletBinding()]
Param(
    [Parameter(Position=1,Mandatory=$true)]
    [string]$PackageId,
    [Parameter()]
    [string]$Version,
    [Parameter()]
    [string]$Source,
    [Parameter()]
    [string]$OutputDirectory="$PSScriptRoot\..\obj\tools",
    [Parameter()]
    [string]$ConfigFile="$PSScriptRoot\..\nuget.config",
    [Parameter()]
    [ValidateSet('quiet','minimal','normal','detailed','diagnostic')]
    [string]$Verbosity='quiet'
)

if (!(Test-Path $OutputDirectory)) { New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null }
$OutputDirectory = (Resolve-Path $OutputDirectory).Path
$ConfigFile = (Resolve-Path $ConfigFile).Path

$packageArg = $PackageId
if ($Version) { $packageArg = "$PackageId@$Version" }

$extraArgs = @()
if ($Source) { $extraArgs += '--source', $Source }

$prevErrorActionPreference = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
& dotnet package download $packageArg --configfile $ConfigFile --output $OutputDirectory --verbosity $Verbosity @extraArgs 2>&1 | Out-Null
$downloadExitCode = $LASTEXITCODE
$ErrorActionPreference = $prevErrorActionPreference

if ($downloadExitCode -ne 0) {
    throw "Failed to download package $packageArg (exit code $downloadExitCode)."
}

# Return the path to the downloaded package directory (dotnet package download uses lowercase id)
$packageIdLower = $PackageId.ToLower()
if ($Version) {
    $packageRoot = Join-Path $OutputDirectory $packageIdLower
    $packageDir = Get-ChildItem -Path $packageRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ieq $Version } |
        Select-Object -First 1
    if ($packageDir) { $packageDir = $packageDir.FullName }
} else {
    # When no version is specified, pick the most recently written version directory.
    $packageRoot = Join-Path $OutputDirectory $packageIdLower
    $packageDir = Get-ChildItem -Path $packageRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object -Property LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($packageDir) { $packageDir = $packageDir.FullName }
}

if ($packageDir -and (Test-Path $packageDir)) {
    Write-Output $packageDir
} else {
    Write-Error "Package directory not found after download."
}
