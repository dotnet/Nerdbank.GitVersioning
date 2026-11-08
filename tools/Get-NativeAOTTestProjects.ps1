#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Gets NativeAOT test executable paths evaluated by the test traversal project.
.PARAMETER Configuration
    The build configuration used to evaluate output paths.
#>
[CmdletBinding()]
Param(
    [string]$Configuration='Debug'
)

$result = & dotnet msbuild "$PSScriptRoot/../test/dirs.proj" `
    -nologo `
    -getTargetResult:GetNativeAOTTests `
    "-property:Configuration=$Configuration"
if ($LASTEXITCODE -ne 0) {
    throw 'Failed to discover NativeAOT tests through MSBuild.'
}

$tests = ($result | Out-String | ConvertFrom-Json).TargetResults.GetNativeAOTTests.Items
foreach ($test in $tests) {
    $test
}
