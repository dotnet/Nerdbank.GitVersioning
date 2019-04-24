<#
.SYNOPSIS
Builds all assets in this repository.
.PARAMETER Configuration
The project configuration to build.
#>
[CmdletBinding(SupportsShouldProcess)]
Param(
    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration,

    [Parameter()]
    [ValidateSet('minimal', 'normal', 'detailed', 'diagnostic')]
    [string]$MsBuildVerbosity = 'minimal'
)

$msbuildCommandLine = "dotnet build `"$PSScriptRoot\src\Nerdbank.GitVersioning.sln`" /m /verbosity:$MsBuildVerbosity /nologo /p:Platform=`"Any CPU`" /t:build,pack"

if (Test-Path "C:\Program Files\AppVeyor\BuildAgent\Appveyor.MSBuildLogger.dll") {
    $msbuildCommandLine += " /logger:`"C:\Program Files\AppVeyor\BuildAgent\Appveyor.MSBuildLogger.dll`""
}

if ($Configuration) {
    $msbuildCommandLine += " /p:Configuration=$Configuration"
}

Push-Location .
try {
    if ($PSCmdlet.ShouldProcess("$PSScriptRoot\src\Nerdbank.GitVersioning.sln", "msbuild")) {
        Invoke-Expression $msbuildCommandLine
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed"
        }
    }

    if ($PSCmdlet.ShouldProcess("$PSScriptRoot\src\nerdbank-gitversioning.npm", "gulp")) {
        cd "$PSScriptRoot\src\nerdbank-gitversioning.npm"
        yarn install
        yarn run build
        if ($LASTEXITCODE -ne 0) {
            throw "Node build failed"
        }
    }
} catch {
    Write-Error "Build failure"
    # we have the try so that PS fails when we get failure exit codes from build steps.
    throw;
} finally {
    Pop-Location
}
