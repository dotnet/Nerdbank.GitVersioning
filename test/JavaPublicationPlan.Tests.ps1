$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..'
$directory = Join-Path $root 'bin\java-publication-plan-tests'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$artifacts = @('nbgv-adapter', 'nbgv-maven-extension', 'nbgv-gradle-plugin', 'com.microsoft.nerdbank-gitversioning.gradle.plugin')
$responses = @{}
$requests = [System.Collections.Generic.List[string]]::new()
function Invoke-WebRequest {
    param($Uri, $Method, [switch]$SkipHttpErrorCheck, $TimeoutSec)
    $requests.Add($Uri)
    if ($Uri -like '*plugins.gradle.org*') {
        return $responses.portal
    }
    $artifact = ($Uri -split '/')[-3]
    return $responses[$artifact]
}
function Assert-Plan {
    param([int]$TaskCount, [bool]$Plugin)
    $plan = & (Join-Path $root 'tools\Get-JavaPublicationPlan.ps1') -Version '0.1.0' -ArtifactDirectory $directory
    if ($plan.CentralTasks.Count -ne $TaskCount -or $plan.PublishPlugin -ne $Plugin) {
        throw "Unexpected publication plan: $($plan | ConvertTo-Json -Compress)"
    }
    return $plan
}
function gh { 'build-sha' }
function git {
    if ($args[0] -eq 'rev-parse') { 'build-sha' }
    elseif ($args[0] -eq 'tag') { $submittedTag }
    else { throw "Unexpected git command: $args" }
}
try {
    foreach ($artifact in $artifacts) {
        Set-Content (Join-Path $directory "$artifact.pom") "<project><groupId>com.microsoft.nerdbank-gitversioning</groupId><artifactId>$artifact</artifactId><version>0.1.0</version></project>"
        $responses[$artifact] = [pscustomobject]@{ StatusCode = 404 }
    }
    $responses.portal = [pscustomobject]@{ StatusCode = 404; Content = '{"errorCode":"UNKNOWN_PLUGIN"}' }
    Assert-Plan 3 $true | Out-Null
    $responses['nbgv-adapter'] = [pscustomobject]@{ StatusCode = 200 }
    $plan = Assert-Plan 2 $true
    if ($plan.CentralTasks -contains ':adapter-core:publishAndReleaseToMavenCentral') {
        throw 'Already-published Central project would be republished.'
    }
    foreach ($artifact in $artifacts) { $responses[$artifact] = [pscustomobject]@{ StatusCode = 200 } }
    Assert-Plan 0 $true | Out-Null
    $responses.portal = [pscustomobject]@{ StatusCode = 200; Content = '{"id":"com.microsoft.nerdbank-gitversioning","version":"0.1.0"}' }
    Assert-Plan 0 $false | Out-Null

    foreach ($case in @('partial', 'central-error', 'portal-error', 'wrong-portal-version')) {
        foreach ($artifact in $artifacts) { $responses[$artifact] = [pscustomobject]@{ StatusCode = 200 } }
        $responses.portal = [pscustomobject]@{ StatusCode = 200; Content = '{"id":"com.microsoft.nerdbank-gitversioning","version":"0.1.0"}' }
        $expected = switch ($case) {
            'partial' { $responses['nbgv-gradle-plugin'] = [pscustomobject]@{ StatusCode = 404 }; 'partially published' }
            'central-error' { $responses['nbgv-adapter'] = [pscustomobject]@{ StatusCode = 503 }; 'Maven Central lookup failed' }
            'portal-error' { $responses.portal = [pscustomobject]@{ StatusCode = 404; Content = '{"errorCode":"INVALID_GRADLE_VERSION"}' }; 'Plugin Portal lookup failed' }
            'wrong-portal-version' { $responses.portal = [pscustomobject]@{ StatusCode = 200; Content = '{"id":"com.microsoft.nerdbank-gitversioning","version":"0.2.0"}' }; 'Plugin Portal lookup failed' }
        }
        try {
            Assert-Plan 0 $false | Out-Null
            throw "Expected failure for $case."
        } catch {
            if ($_.Exception.Message -notmatch $expected) { throw }
        }
    }

    $workflow = Get-Content (Join-Path $root '.github\workflows\release.yml') -Raw
    $step = [regex]::Match($workflow, '(?ms)^    - name: Plan Java publication and validate prerequisites\r?\n.*?(?=^    - name:)').Value
    $run = [regex]::Match($step, '(?m)^      run: \|\r?\n(?<run>(?:        .*\r?\n)+)').Groups['run'].Value
    if (-not $run) { throw 'Cannot find the Java publication planning step.' }
    $run = [regex]::Replace($run, '(?m)^        ', '')
    $packageFile = Join-Path $directory 'packages.json'
    Set-Content -LiteralPath $packageFile '{"version":"0.1.0"}'
    $javaDirectory = Join-Path $directory 'java'
    New-Item -ItemType Directory -Path $javaDirectory | Out-Null
    Compress-Archive -Path (Join-Path $directory '*.pom') -DestinationPath (Join-Path $javaDirectory 'nbgv-java-maven-repository-0.1.0.zip')
    $extracted = Join-Path $directory 'nbgv-java-maven-repository'
    $outputFile = Join-Path $directory 'workflow-output.txt'
    $run = $run.Replace('${{ runner.temp }}/deployables/ecosystems/java', $javaDirectory.Replace("'", "''"))
    $run = $run.Replace('${{ runner.temp }}/deployables/ecosystems/packages.json', $packageFile.Replace("'", "''"))
    $run = $run.Replace('${{ runner.temp }}', $directory.Replace("'", "''"))
    $run = $run.Replace('$env:GITHUB_ENV', '$outputFile')
    $run = $run.Replace('[Environment]::GetEnvironmentVariable($name)', '$credentials[$name]')
    $credentials = @{
        MAVEN_CENTRAL_USERNAME = 'test'; MAVEN_CENTRAL_PASSWORD = 'test'
        MAVEN_SIGNING_KEY = 'test'; MAVEN_SIGNING_PASSWORD = 'test'
        GRADLE_PUBLISH_KEY = 'test'; GRADLE_PUBLISH_SECRET = 'test'
    }
    foreach ($case in @('new', 'retry-portal', 'published', 'submitted')) {
        $submittedTag = if ($case -eq 'submitted') { 'java/v0.1.0' } else { $null }
        foreach ($artifact in $artifacts) {
            $responses[$artifact] = [pscustomobject]@{ StatusCode = $(if ($case -eq 'new') { 404 } else { 200 }) }
        }
        $responses.portal = if ($case -in @('new', 'retry-portal')) {
            [pscustomobject]@{ StatusCode = 404; Content = '{"errorCode":"UNKNOWN_PLUGIN_VERSION"}' }
        } else {
            [pscustomobject]@{ StatusCode = 200; Content = '{"id":"com.microsoft.nerdbank-gitversioning","version":"0.1.0"}' }
        }
        $requests.Clear()
        if (Test-Path -LiteralPath $extracted) {
            Get-ChildItem -LiteralPath $extracted -File | Remove-Item
            Remove-Item -LiteralPath $extracted
        }
        Set-Content -LiteralPath $outputFile ''
        Invoke-Expression $run
        $output = Get-Content -LiteralPath $outputFile -Raw
        $central = $case -eq 'new'
        $portal = $case -in @('new', 'retry-portal')
        if ($output -notmatch "PUBLISH_JAVA_CENTRAL=$central" -or $output -notmatch "PUBLISH_JAVA_PLUGIN=$portal") {
            throw "Incorrect workflow upload selection for $case."
        }
        if ($case -eq 'submitted' -and ($requests.Count -ne 0 -or $output -notmatch 'RECORD_JAVA_SUBMISSION=false')) {
            throw 'Submitted versions must skip registry requests and tag creation.'
        }
        if ($case -ne 'submitted' -and $output -notmatch 'RECORD_JAVA_SUBMISSION=true') {
            throw "Successful $case publication would not be recorded."
        }
    }
    Write-Host 'Java publication plan tests passed.'
} finally {
    foreach ($artifact in $artifacts) { Remove-Item -LiteralPath (Join-Path $directory "$artifact.pom") }
    foreach ($name in @('packages.json', 'workflow-output.txt')) {
        $path = Join-Path $directory $name
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    }
    if (Test-Path -LiteralPath $extracted) {
        Get-ChildItem -LiteralPath $extracted -File | Remove-Item
        Remove-Item -LiteralPath $extracted
    }
    if (Test-Path -LiteralPath $javaDirectory) {
        Remove-Item -LiteralPath (Join-Path $javaDirectory 'nbgv-java-maven-repository-0.1.0.zip')
        Remove-Item -LiteralPath $javaDirectory
    }
    Remove-Item -LiteralPath $directory
}
