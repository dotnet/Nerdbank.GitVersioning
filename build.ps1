$msbuildCommandLine = "msbuild `"$PSScriptRoot\src\Nerdbank.GitVersioning.sln`" /m /verbosity:minimal /nologo"

if (Test-Path "C:\Program Files\AppVeyor\BuildAgent\Appveyor.MSBuildLogger.dll") {
    $msbuildCommandLine += " /logger:`"C:\Program Files\AppVeyor\BuildAgent\Appveyor.MSBuildLogger.dll`""
}

try {
    Invoke-Expression $msbuildCommandLine
    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild failed"
    }

    Push-Location "$PSScriptRoot\src\nerdbank-gitversioning.npm"
    .\node_modules\.bin\gulp.cmd
    if ($LASTEXITCODE -ne 0) {
        throw "Node build failed"
    }

    Pop-Location
} catch {
    Write-Error "Build failure"
    # we have the try so that PS fails when we get failure exit codes from build steps.
    throw;
}