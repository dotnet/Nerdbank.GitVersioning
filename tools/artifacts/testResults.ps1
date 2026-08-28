[CmdletBinding()]
Param(
)

$result = @{}

# Hang and crash dumps are copied into the TRX attachment directory when a TRX report is requested.
# Prefer that copy to avoid uploading the same dump twice, but keep the original when no attachment
# copy exists (e.g. GitHub Actions runs, which don't request a TRX report) so crashes stay diagnosable.
function Select-TestResultFile($files) {
    $attachedDumpNames = @($files | Where-Object { $_.Extension -eq '.dmp' -and $_.FullName -match '[/\\]In[/\\]' } | ForEach-Object { $_.Name })
    $files | Where-Object { $_.Extension -ne '.dmp' -or $_.FullName -match '[/\\]In[/\\]' -or $attachedDumpNames -notcontains $_.Name }
}

$RepoRoot = Resolve-Path "$PSScriptRoot\..\.."
$testRoot = Join-Path $RepoRoot test
$legacyTestResults = Join-Path $testRoot TestResults
if (Test-Path $legacyTestResults) {
    $result[$testRoot] = Select-TestResultFile @(Get-ChildItem $legacyTestResults -Recurse -Directory | Get-ChildItem -Recurse -File)
}

$artifactStaging = & "$PSScriptRoot/../Get-ArtifactsStagingDirectory.ps1"
$testlogsPath = Join-Path $artifactStaging "test_logs"
if (Test-Path $testlogsPath) {
    $result[$testlogsPath] = Select-TestResultFile @(Get-ChildItem $testlogsPath -Recurse)
}

$result
