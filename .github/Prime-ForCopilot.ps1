if ((git -C $PSScriptRoot rev-parse --is-shallow-repository) -eq 'true')
{
    Write-Host "Shallow clone detected, disabling NBGV Git engine so the build can succeed."
    $env:NBGV_GitEngine='Disabled'
}
