$msbuildCommandLine = "msbuild `"$PSScriptRoot\src\Nerdbank.GitVersioning.sln`" /m /verbosity:minimal /nologo"

if (Test-Path "C:\Program Files\AppVeyor\BuildAgent\Appveyor.MSBuildLogger.dll") {
    $msbuildCommandLine += ` /logger:"C:\Program Files\AppVeyor\BuildAgent\Appveyor.MSBuildLogger.dll"`
}

Invoke-Expression $msbuildCommandLine 
Push-Location "$PSScriptRoot\src\nerdbank-gitversioning.npm"
gulp
Pop-Location
