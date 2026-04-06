[CmdletBinding()]
Param(
)

$result = @{}

$RepoRoot = Resolve-Path "$PSScriptRoot\..\.."
$testRoot = Join-Path $RepoRoot test
$result[$testRoot] = (Get-ChildItem "$testRoot\TestResults" -Recurse -Directory | Get-ChildItem -Recurse -File)

$artifactStaging = & "$PSScriptRoot/../Get-ArtifactsStagingDirectory.ps1"
$testlogsPath = Join-Path $artifactStaging "test_logs"
if (Test-Path $testlogsPath) {
    $result[$testlogsPath] = Get-ChildItem $testlogsPath -Recurse;
}

$result
