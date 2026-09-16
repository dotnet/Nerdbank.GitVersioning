[CmdletBinding(SupportsShouldProcess)]
Param(
    [Parameter()]
    [string]$Version
)

$repoRoot = Split-Path $PSScriptRoot -Parent
$manifestPath = Join-Path $repoRoot 'src/nerdbank-gitversioning-rs/Cargo.toml'

if (!$Version) {
    $Version = dotnet tool run nbgv get-version -v SemVer2
    if ($LASTEXITCODE -ne 0) {
        throw 'nbgv get-version failed.'
    }
}

$manifest = Get-Content $manifestPath -Raw
$patterns = @(
    '(?m)(?<=^\[workspace\.package\]\r?\n)version = "[^"]+"',
    '(?m)(?<=^nerdbank-gitversioning = \{ version = ")[^"]+(?=", path = "\." \}\r?$)'
)

foreach ($pattern in $patterns) {
    if ([regex]::Matches($manifest, $pattern).Count -ne 1) {
        throw "Expected exactly one match for '$pattern' in $manifestPath."
    }
}

$manifest = [regex]::Replace($manifest, $patterns[0], "version = `"$Version`"")
$manifest = [regex]::Replace($manifest, $patterns[1], $Version)

if ($PSCmdlet.ShouldProcess($manifestPath, "Set Cargo package versions to $Version")) {
    [System.IO.File]::WriteAllText($manifestPath, $manifest)
}
