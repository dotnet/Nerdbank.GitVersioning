$RepoRoot = Resolve-Path "$PSScriptRoot\..\.."

$coverageFilesUnderRoot = @(Get-ChildItem "$RepoRoot/*.cobertura.xml" -Recurse | Where-Object {$_.FullName -notlike "*/In/*"  -and $_.FullName -notlike "*\In\*" })

# Under MTP, coverage files are written directly to the artifacts output directory,
# so we need to look there too.
$ArtifactStagingFolder = & "$PSScriptRoot/../Get-ArtifactsStagingDirectory.ps1"
$directTestLogs = Join-Path $ArtifactStagingFolder test_logs
$coverageFilesUnderArtifacts = if (Test-Path $directTestLogs) { @(Get-ChildItem "$directTestLogs/*.cobertura.xml" -Recurse) } else { @() }

# Prepare code coverage reports for merging on another machine
Write-Host "Substituting $repoRoot with `"{reporoot}`""
@($coverageFilesUnderRoot + $coverageFilesUnderArtifacts) |? { $_ }|% {
    $content = Get-Content -LiteralPath $_ |% { $_ -Replace [regex]::Escape($repoRoot), "{reporoot}" }
    Set-Content -LiteralPath $_ -Value $content -Encoding UTF8
}

if (!((Test-Path $RepoRoot\bin) -and (Test-Path $RepoRoot\obj))) { return }

@{
    $directTestLogs = $coverageFilesUnderArtifacts;
    $RepoRoot = (
        $coverageFilesUnderRoot +
        (Get-ChildItem "$RepoRoot\obj\*.cs" -Recurse)
    );
}
