[CmdletBinding()]
Param(
)

$result = @{}

$testRoot = Resolve-Path "$PSScriptRoot\..\..\test"
$result[$testRoot] = (Get-ChildItem "$testRoot\TestResults" -Recurse -Directory | Get-ChildItem -Recurse -File)


$artifactStaging = & "$PSScriptRoot\..\Get-ArtifactsStagingDirectory.ps1"
$testlogsPath = Join-Path $artifactStaging "test_logs"
Write-Host "Searching $testlogsPath for anything"
if (Test-Path $testlogsPath) {
    Write-Host "test log path exists"
    $result[$testlogsPath] = Get-ChildItem "$testlogsPath\*";
}

$result
