<#
.SYNOPSIS
    Uploads code coverage to codecov.io
.PARAMETER CodeCovToken
    Code coverage token to use
.PARAMETER PathToCodeCoverage
    Path to root of code coverage files
.PARAMETER Name
    Name to upload with codecoverge
.PARAMETER Flags
    Flags to upload with codecoverge
#>
[CmdletBinding()]
Param (
    [Parameter(Mandatory=$true)]
    [string]$CodeCovToken,
    [Parameter(Mandatory=$true)]
    [string]$PathToCodeCoverage,
    [string]$Name,
    [string]$Flags
)

$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path
$codeCovTool = & "$PSScriptRoot/Get-CodeCovTool.ps1"
$coverageFiles = @(Get-ChildItem -Recurse -LiteralPath $PathToCodeCoverage -Filter "*.cobertura.xml")
if ($coverageFiles.Count -eq 0) {
    return
}

$arguments = @(
    "upload-process",
    "--disable-search",
    "--fail-on-error",
    "-t", $CodeCovToken,
    "--network-root-folder", $RepoRoot
)
foreach ($coverageFile in $coverageFiles) {
    $relativeFilePath = Resolve-Path -Relative $coverageFile.FullName
    Write-Host "Uploading: $relativeFilePath" -ForegroundColor Yellow
    $arguments += @("-f", $relativeFilePath)
}

if ($Flags) {
    $arguments += @("-F", $Flags)
}

if ($Name) {
    $arguments += @("-n", $Name)
}

& $codeCovTool @arguments
if ($LASTEXITCODE -ne 0) {
    Write-Error "Codecov CLI failed with exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}
