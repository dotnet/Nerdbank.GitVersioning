[CmdletBinding()]
Param(
)

$result = @{}

$RepoRoot = Resolve-Path "$PSScriptRoot\..\.."
$testRoot = Join-Path $RepoRoot test
$legacyTestResults = Join-Path $testRoot TestResults
if (Test-Path $legacyTestResults) {
    $result[$testRoot] = Get-ChildItem $legacyTestResults -Recurse -Directory |
        Get-ChildItem -Recurse -File |
        Where-Object { $_.Extension -ne '.dmp' -or $_.FullName -match '\\In\\' }
}

$artifactStaging = & "$PSScriptRoot/../Get-ArtifactsStagingDirectory.ps1"
$testlogsPath = Join-Path $artifactStaging "test_logs"
if (Test-Path $testlogsPath) {
    # Hang and crash dumps are copied into the TRX attachment directory.
    $result[$testlogsPath] = Get-ChildItem $testlogsPath -Recurse |
        Where-Object { $_.Extension -ne '.dmp' -or $_.FullName -match '\\In\\' }
}

$result
