$RepoRoot = [System.IO.Path]::GetFullPath("$PSScriptRoot\..\..")
$BuildConfiguration = $env:BUILDCONFIGURATION
if (!$BuildConfiguration) {
    $BuildConfiguration = 'Debug'
}

$PackagesRoot = "$RepoRoot/bin/Packages/$BuildConfiguration"
$JsRoot = "$RepoRoot/bin/js"

if (!(Test-Path $PackagesRoot))  { return }

@{
    "$PackagesRoot" = (Get-ChildItem $PackagesRoot -Recurse -Exclude *.LKG.*.nupkg);
    "$JsRoot" = (Get-ChildItem $JsRoot *.tgz);
}
