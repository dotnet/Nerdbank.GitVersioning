#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Runs tests as they are run in cloud test runs.
.PARAMETER Configuration
    The configuration within which to run tests
.PARAMETER Agent
    The name of the agent. This is used in preparing test run titles.
.PARAMETER PublishResults
    A switch to publish results to Azure Pipelines.
.PARAMETER dotnet32
    The path to the 32-bit dotnet.exe to use to run tests. If not specified, the default (typically 64-bit) dotnet process will be used.
#>
Param(
    [string]$Configuration='Debug',
    [string]$Agent='Local',
    [switch]$PublishResults,
    [string]$dotnet32
)

$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path
$ArtifactStagingFolder = & "$PSScriptRoot/Get-ArtifactsStagingDirectory.ps1"

$dotnet = 'dotnet'
if ($dotnet32) {
  $dotnet = $dotnet32
  $x86RunTitle = ", x86"
}

& $dotnet test $RepoRoot `
    --no-build `
    -c $Configuration `
    --filter "TestCategory!=FailsInCloudTest" `
    -p:CollectCoverage=true `
    --blame-hang-timeout 60s `
    --blame-crash `
    -bl:"$ArtifactStagingFolder/build_logs/test.binlog" `
    --diag "$ArtifactStagingFolder/test_logs/diag.log;TraceLevel=info" `
    --logger trx `
    -- `
    RunConfiguration.DisableAppDomain=true

$unknownCounter = 0
Get-ChildItem -Recurse -Path $RepoRoot\test\*.trx |% {
  Copy-Item $_ -Destination $ArtifactStagingFolder/test_logs/

  if ($PublishResults) {
    $x = [xml](Get-Content -Path $_)
    $runTitle = $null
    if ($x.TestRun.TestDefinitions -and $x.TestRun.TestDefinitions.GetElementsByTagName('UnitTest')) {
      $storage = $x.TestRun.TestDefinitions.GetElementsByTagName('UnitTest')[0].storage -replace '\\','/'
      if ($storage -match '/(?<tfm>net[^/]+)/(?:(?<rid>[^/]+)/)?(?<lib>[^/]+)\.dll$') {
        if ($matches.rid) {
          $runTitle = "$($matches.lib) ($($matches.tfm), $($matches.rid), $Agent$x86RunTitle)"
        } else {
          $runTitle = "$($matches.lib) ($($matches.tfm), $Agent$x86RunTitle)"
        }
      }
    }
    if (!$runTitle) {
      $unknownCounter += 1;
      $runTitle = "unknown$unknownCounter ($Agent$x86RunTitle)";
    }

    Write-Host "##vso[results.publish type=VSTest;runTitle=$runTitle;publishRunAttachments=true;resultFiles=$_;failTaskOnFailedTests=true;testRunSystem=VSTS - PTR;]"
  }
}
