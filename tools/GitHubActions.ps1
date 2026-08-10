function Add-GitHubActionsEnvVariable {
    param(
        [string]$Path = $env:GITHUB_ENV,
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "GitHub Actions GITHUB_ENV file path must not be empty."
    }

    if ([string]::IsNullOrWhiteSpace($Name)) {
        throw "GitHub Actions environment variable name must not be empty."
    }

    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    $delimiter = [guid]::NewGuid().ToString('N')
    [System.IO.File]::AppendAllText($Path, "$Name<<$delimiter`n$Value`n$delimiter`n", $utf8NoBom)
}

function Add-GitHubActionsPath {
    param(
        [string]$Path = $env:GITHUB_PATH,
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "GitHub Actions GITHUB_PATH file path must not be empty."
    }

    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "GitHub Actions path entry must not be empty."
    }

    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::AppendAllText($Path, "$Value`n", $utf8NoBom)
}
