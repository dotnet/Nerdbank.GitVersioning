function Add-GitHubActionsEnvVariable {
    param(
        [string]$Path = $env:GITHUB_ENV,
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Name)) {
        throw "GitHub Actions environment variable names must not be empty."
    }

    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    $delimiter = [guid]::NewGuid().ToString('N')
    [System.IO.File]::AppendAllText($Path, "$Name<<$delimiter`n$Value`n$delimiter`n", $utf8NoBom)
}
