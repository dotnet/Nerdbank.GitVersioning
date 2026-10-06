#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Runs tests as they are run in cloud test runs.
.PARAMETER Configuration
    The configuration within which to run tests
.PARAMETER IncludeNativeAOT
    Runs the NativeAOT-compiled tests and fails if the expected image is missing.
.PARAMETER Agent
    The name of the agent. This is used in preparing test run titles.
.PARAMETER PublishResults
    A switch to publish results to Azure Pipelines.
.PARAMETER x86
    A switch to run the tests in an x86 process.
.PARAMETER dotnet32
    The path to a 32-bit dotnet executable to use.
.PARAMETER NoCoverage
    A switch to skip code coverage collection.
#>
[CmdletBinding()]
Param(
    [string]$Configuration='Debug',
    [switch]$IncludeNativeAOT,
    [string]$Agent='Local',
    [switch]$PublishResults,
    [switch]$x86,
    [string]$dotnet32,
    [switch]$NoCoverage
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
      exit 1
    }
  }
}

$testBinLog = Join-Path $ArtifactStagingFolder (Join-Path build_logs test.binlog)
$testLogs = Join-Path $ArtifactStagingFolder test_logs
if (Test-Path -LiteralPath $testLogs) {
    Remove-Item -LiteralPath $testLogs -Recurse -Force
}

$globalJson = Get-Content $PSScriptRoot/../global.json | ConvertFrom-Json
$isMTP = $globalJson.test.runner -eq 'Microsoft.Testing.Platform'
$extraArgs = @()
$failedTests = 0
$publishTrx = $PublishResults -and $env:TF_BUILD

if ($isMTP) {
    if ($OnCI) { $extraArgs += '--no-progress' }

    $dumpSwitches = @(
        ,'--hangdump'
        ,'--hangdump-timeout','5m'
        ,'--crashdump'
        ,'--crashdump-type','Heap'
        # The native crash report accompanies the dump and is often the only way to identify the
        # faulting thread and instruction when a test host dies of an access violation on Linux.
        ,'--crash-report-if-supported'
    )
    # Directory-valued options use the --option=value form. 'dotnet test' rejects a separate argument naming an existing directory
    # ("Specifying a directory for 'dotnet test' should be via '--project' or '--solution'"), even after '--',
    # and $testLogs exists once the first test project below has run.
    $mtpArgs = @(
        ,'--diagnostic'
        ,"--diagnostic-output-directory=$testLogs"
        ,'--diagnostic-verbosity','Information'
        ,"--results-directory=$testLogs"
    )

    if (-not $NoCoverage) {
        $mtpArgs += @(
            ,'--coverage'
            ,'--coverage-output-format','cobertura'
            ,'--coverage-settings',"$PSScriptRoot/test.runsettings"
        )
    }

    $solutionFiles = @(Get-ChildItem -LiteralPath $RepoRoot -File | Where-Object { $_.Extension -in '.sln', '.slnx' })
    if ($solutionFiles.Count -ne 1) {
        throw "Expected exactly one solution file in $RepoRoot, but found $($solutionFiles.Count)."
    }

    $solutionPath = $solutionFiles[0].FullName

    # This repo's test projects use different test frameworks (TUnit, plus xunit for the tests that need Xunit.Combinatorial),
    # which take different filter syntax. Each test project declares its FailsInCloudTest filter in its CloudTestFilterOption and
    # CloudTestFilterValue MSBuild properties (see test/Directory.Build.props), so each test project is run separately.
    $projectPaths = @(& dotnet sln $solutionPath list | Where-Object { $_ -match '\.(cs|vb|fs)proj$' } |% { Join-Path $RepoRoot $_.Trim() })
    foreach ($projectPath in $projectPaths) {
        $projectProperties = (& dotnet msbuild $projectPath -getProperty:IsTestingPlatformApplication -getProperty:CloudTestFilterOption -getProperty:CloudTestFilterValue | ConvertFrom-Json).Properties
        if ($projectProperties.IsTestingPlatformApplication -ne 'true') { continue }

        $projectName = [IO.Path]::GetFileNameWithoutExtension($projectPath)
        $projectBinLog = Join-Path (Split-Path $testBinLog) "test_$projectName.binlog"
        $filterOption = $projectProperties.CloudTestFilterOption
        $filterValue = $projectProperties.CloudTestFilterValue

        # The filter is passed as quoted scalars: on Linux and macOS, PowerShell expands wildcards in array (splatted) arguments as file paths.
        & $dotnet test --project $projectPath `
            --no-build `
            -c $Configuration `
            -bl:"$projectBinLog" `
            -- `
            "$filterOption" `
            "$filterValue" `
            @mtpArgs `
            @dumpSwitches `
            @extraArgs
        if ($LASTEXITCODE -ne 0) { $failedTests += 1 }
    }

    if ($IncludeNativeAOT) {
        $nativeAotTests = @(& "$PSScriptRoot/Get-NativeAOTTestProjects.ps1" -Configuration $Configuration)
        foreach ($nativeAotTest in $nativeAotTests) {
            $testExecutable = $nativeAotTest.ExecutablePath
            if (-not (Test-Path -LiteralPath $testExecutable -PathType Leaf)) {
                Write-Error "Expected NativeAOT TUnit test executable '$testExecutable' was not found."
                $failedTests += 1
                continue
            }

            $nativeAotArgs = @(
                ,'--diagnostic'
                ,'--diagnostic-output-directory',$testLogs
                ,'--diagnostic-verbosity','Information'
                ,'--results-directory',$testLogs
                ,'--report-trx'
                ,'--report-trx-filename',"$($nativeAotTest.ProjectName)_$($nativeAotTest.TargetFramework)_NativeAOT_{arch}.trx"
            )
            if ($IsWindows) {
                $nativeAotArgs += $dumpSwitches
            }
            Write-Host "Running NativeAOT TUnit tests from '$testExecutable'." -ForegroundColor Cyan
            & $testExecutable @nativeAotArgs @extraArgs
            if ($LASTEXITCODE -ne 0) { $failedTests += 1 }
        }
    }

    $trxFiles = @(Get-ChildItem -Recurse -Path $testLogs\*.trx -ErrorAction Ignore)
} else {
    $testDiagLog = Join-Path $ArtifactStagingFolder (Join-Path test_logs diag.log)
    $vstestArgs = @(
        '--blame-hang-timeout', '60s',
        '--blame-crash',
        '-bl:"$testBinLog"',
        '--diag', "$testDiagLog;TraceLevel=info"
    )
    if (-not $NoCoverage) {
        $vstestArgs += @(
            '--collect', 'Code Coverage;Format=cobertura',
            '--settings', "$PSScriptRoot/test.runsettings"
        )
    }
    if ($publishTrx) {
        $vstestArgs += '--logger'
        $vstestArgs += 'trx'
    }

    & $dotnet test $RepoRoot `
        --no-build `
        -c $Configuration `
        --filter "TestCategory!=FailsInCloudTest" `
        @vstestArgs `
        @extraArgs
    if ($LASTEXITCODE -ne 0) { $failedTests += 1 }

    $trxFiles = @(Get-ChildItem -Recurse -Path $RepoRoot\test\*.trx -ErrorAction Ignore)
}

$unknownCounter = 0
$trxFiles |% {
  New-Item $testLogs -ItemType Directory -Force | Out-Null
  if (!($_.FullName.StartsWith($testLogs, [StringComparison]::OrdinalIgnoreCase))) {
    Copy-Item $_ -Destination $testLogs
  }

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

if ($failedTests -ne 0) {
    exit $failedTests
}
