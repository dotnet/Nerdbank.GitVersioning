[CmdletBinding()]
Param(
)

$result = @{}

$testRoot = Resolve-Path "$PSScriptRoot\..\..\test"
$result[$testRoot] = (Get-ChildItem "$testRoot\TestResults" -Recurse -Directory | Get-ChildItem -Recurse -File)

$artifactStaging = & "$PSScriptRoot\..\Get-ArtifactsStagingDirectory.ps1"
$testlogsPath = Join-Path $artifactStaging "test_logs"
if (Test-Path $testlogsPath) {
    $result[$testlogsPath] = Get-ChildItem "$testlogsPath\*";
}

$result
