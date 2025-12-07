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
.PARAMETER x86
    A switch to run the tests in an x86 process.
.PARAMETER dotnet32
    The path to a 32-bit dotnet executable to use.
#>
[CmdletBinding()]
Param(
    [string]$Configuration='Debug',
    [string]$Agent='Local',
    [switch]$PublishResults,
    [switch]$x86,
    [string]$dotnet32
)

$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path
$ArtifactStagingFolder = & "$PSScriptRoot/Get-ArtifactsStagingDirectory.ps1"
$OnCI = ($env:CI -or $env:TF_BUILD)

$dotnet = 'dotnet'
if ($x86) {
  $x86RunTitleSuffix = ", x86"
  if ($dotnet32) {
    $dotnet = $dotnet32
  } else {
    $dotnet32Possibilities = "$PSScriptRoot\../obj/tools/x86/.dotnet/dotnet.exe", "$env:AGENT_TOOLSDIRECTORY/x86/dotnet/dotnet.exe", "${env:ProgramFiles(x86)}\dotnet\dotnet.exe"
    $dotnet32Matches = $dotnet32Possibilities |? { Test-Path $_ }
    if ($dotnet32Matches) {
      $dotnet = Resolve-Path @($dotnet32Matches)[0]
      Write-Host "Running tests using `"$dotnet`"" -ForegroundColor DarkGray
    } else {
      Write-Error "Unable to find 32-bit dotnet.exe"
      return 1
    }
  }
}

$testBinLog = Join-Path $ArtifactStagingFolder (Join-Path build_logs test.binlog)
$testLogs = Join-Path $ArtifactStagingFolder test_logs

$globalJson = Get-Content $PSScriptRoot/../global.json | ConvertFrom-Json
$isMTP = $globalJson.test.runner -eq 'Microsoft.Testing.Platform'
if ($isMTP) {
    $extraArgs = @()
    if ($OnCI) { $extraArgs += '--no-progress' }
    & $dotnet test --solution $RepoRoot `
        --no-build `
        -c $Configuration `
        -bl:"$testBinLog" `
        --filter-not-trait 'TestCategory=FailsInCloudTest' `
        --coverage `
        --coverage-output-format cobertura `
        --coverage-settings "$PSScriptRoot/test.runsettings" `
        --hangdump `
        --hangdump-timeout 60s `
        --crashdump `
        --diagnostic `
        --diagnostic-output-directory $testLogs `
        --diagnostic-verbosity Information `
        --results-directory $testLogs `
        --report-trx `
        @extraArgs
} else {
    $testDiagLog = Join-Path $ArtifactStagingFolder (Join-Path test_logs diag.log)
    & $dotnet test $RepoRoot `
        --no-build `
        -c $Configuration `
        --filter "TestCategory!=FailsInCloudTest" `
        --collect "Code Coverage;Format=cobertura" `
        --settings "$PSScriptRoot/test.runsettings" `
        --blame-hang-timeout 60s `
        --blame-crash `
        -bl:"$testBinLog" `
        --diag "$testDiagLog;TraceLevel=info" `
        --logger trx `
}

$unknownCounter = 0
Get-ChildItem -Recurse -Path $RepoRoot\test\*.trx |% {
  New-Item $testLogs -ItemType Directory -Force | Out-Null
  Copy-Item $_ -Destination $testLogs

  if ($PublishResults) {
    $x = [xml](Get-Content -LiteralPath $_)
    $runTitle = $null
    if ($x.TestRun.TestDefinitions -and $x.TestRun.TestDefinitions.GetElementsByTagName('UnitTest')) {
      $storage = $x.TestRun.TestDefinitions.GetElementsByTagName('UnitTest')[0].storage -replace '\\','/'
      if ($storage -match '/(?<tfm>net[^/]+)/(?:(?<rid>[^/]+)/)?(?<lib>[^/]+)\.(dll|exe)$') {
        if ($matches.rid) {
          $runTitle = "$($matches.lib) ($($matches.tfm), $($matches.rid), $Agent)"
        } else {
          $runTitle = "$($matches.lib) ($($matches.tfm)$x86RunTitleSuffix, $Agent)"
        }
      }
    }
    if (!$runTitle) {
      $unknownCounter += 1;
      $runTitle = "unknown$unknownCounter ($Agent$x86RunTitleSuffix)";
    }

    Write-Host "##vso[results.publish type=VSTest;runTitle=$runTitle;publishRunAttachments=true;resultFiles=$_;failTaskOnFailedTests=true;testRunSystem=VSTS - PTR;]"
  }
}
