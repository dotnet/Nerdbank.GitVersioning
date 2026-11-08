[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Version,
    [Parameter(Mandatory)]
    [string]$ArtifactDirectory
)

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$' -or $Version -match '(?i)SNAPSHOT') {
    throw "Invalid Java release version '$Version'."
}

$projects = @{
    'nbgv-adapter' = 'adapter-core'
    'nbgv-maven-extension' = 'maven-extension'
    'nbgv-gradle-plugin' = 'gradle-plugin'
    'com.microsoft.nerdbank-gitversioning.gradle.plugin' = 'gradle-plugin'
}
$states = @{}
$seen = @{}
foreach ($pomFile in Get-ChildItem -LiteralPath $ArtifactDirectory -Filter '*.pom' -Recurse) {
    [xml]$pom = Get-Content -LiteralPath $pomFile.FullName -Raw
    $group = $pom.SelectSingleNode("/*[local-name()='project']/*[local-name()='groupId']").InnerText
    $artifact = $pom.SelectSingleNode("/*[local-name()='project']/*[local-name()='artifactId']").InnerText
    $pomVersion = $pom.SelectSingleNode("/*[local-name()='project']/*[local-name()='version']").InnerText
    if ($group -ne 'com.microsoft.nerdbank-gitversioning' -or -not $projects.ContainsKey($artifact) -or $pomVersion -ne $Version -or $seen.ContainsKey($artifact)) {
        throw "Unexpected Java publication coordinates in $($pomFile.FullName)."
    }
    $seen[$artifact] = $true
    $uri = "https://repo.maven.apache.org/maven2/$($group.Replace('.', '/'))/$artifact/$Version/$artifact-$Version.pom"
    $response = Invoke-WebRequest -Uri $uri -Method Head -SkipHttpErrorCheck -TimeoutSec 60
    switch ([int]$response.StatusCode) {
        200 { $published = $true }
        404 { $published = $false }
        default { throw "Maven Central lookup failed for $uri (HTTP $($response.StatusCode))." }
    }
    $project = $projects[$artifact]
    if (-not $states.ContainsKey($project)) {
        $states[$project] = @()
    }
    $states[$project] = @($states[$project]) + $published
}
if ($seen.Count -ne $projects.Count) {
    throw "Expected all four Java publication POMs in $ArtifactDirectory."
}

$tasks = @()
foreach ($project in $states.Keys | Sort-Object) {
    $published = @($states[$project] | Where-Object { $_ -eq $true }).Count
    if ($published -eq 0) {
        $tasks += ":${project}:publishAndReleaseToMavenCentral"
    } elseif ($published -ne $states[$project].Count) {
        throw "Java version $Version is partially published for $project. Recover the incomplete Central deployment or advance the Java version; immutable artifacts cannot be overwritten."
    }
}

# The Portal's legacy lookup API accepts Gradle 4.0, not current Gradle versions.
$plugin = 'com.microsoft.nerdbank-gitversioning'
$response = Invoke-WebRequest -Uri "https://plugins.gradle.org/api/gradle/4.0/plugin/use/$plugin/$Version" -SkipHttpErrorCheck -TimeoutSec 60
$result = $response.Content | ConvertFrom-Json
if ([int]$response.StatusCode -eq 200 -and $result.id -eq $plugin -and $result.version -eq $Version) {
    $publishPlugin = $false
} elseif ([int]$response.StatusCode -eq 404 -and $result.errorCode -in @('UNKNOWN_PLUGIN', 'UNKNOWN_PLUGIN_VERSION')) {
    $publishPlugin = $true
} else {
    throw "Plugin Portal lookup failed (HTTP $($response.StatusCode)): $($response.Content)"
}

[pscustomobject]@{
    CentralTasks = $tasks
    PublishPlugin = $publishPlugin
}
