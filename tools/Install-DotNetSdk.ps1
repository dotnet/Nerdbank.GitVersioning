#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Installs the .NET SDK specified in the global.json file at the root of this repository,
    along with supporting .NET runtimes used for testing.
.DESCRIPTION
    This MAY not require elevation, as the SDK and runtimes are installed locally to this repo location,
    unless `-InstallLocality machine` is specified.
.PARAMETER InstallLocality
    A value indicating whether dependencies should be installed locally to the repo or at a per-user location.
    Per-user allows sharing the installed dependencies across repositories and allows use of a shared expanded package cache.
    Visual Studio will only notice and use these SDKs/runtimes if VS is launched from the environment that runs this script.
    Per-repo allows for high isolation, allowing for a more precise recreation of the environment within an Azure Pipelines build.
    When using 'repo', environment variables are set to cause the locally installed dotnet SDK to be used.
    Per-repo can lead to file locking issues when dotnet.exe is left running as a build server and can be mitigated by running `dotnet build-server shutdown`.
    Per-machine requires elevation and will download and install all SDKs and runtimes to machine-wide locations so all applications can find it.
.PARAMETER SdkOnly
    Skips installing the runtime.
.PARAMETER IncludeX86
    Installs a x86 SDK and runtimes in addition to the x64 ones. Only supported on Windows. Ignored on others.
.PARAMETER IncludeAspNetCore
    Installs the ASP.NET Core runtime along with the .NET runtime.
#>
[CmdletBinding(SupportsShouldProcess=$true,ConfirmImpact='Medium')]
Param (
    [ValidateSet('repo','user','machine')]
    [string]$InstallLocality='user',
    [switch]$SdkOnly,
    [switch]$IncludeX86,
    [switch]$IncludeAspNetCore
)

$DotNetInstallScriptRoot = "$PSScriptRoot/../obj/tools"
if (!(Test-Path $DotNetInstallScriptRoot)) { New-Item -ItemType Directory -Path $DotNetInstallScriptRoot -WhatIf:$false | Out-Null }
$DotNetInstallScriptRoot = Resolve-Path $DotNetInstallScriptRoot

# Look up actual required .NET SDK version from global.json
$sdks = @(New-Object PSObject -Property @{ Version = & "$PSScriptRoot/variables/DotNetSdkVersion.ps1" })

# Sometimes a repo requires extra SDKs to be installed (e.g. msbuild.locator scenarios running in tests).
# In such a circumstance, a precise SDK version or a channel can be added as in the example below:
# $sdks += New-Object PSObject -Property @{ Channel = '8.0' }

If ($IncludeX86 -and ($IsMacOS -or $IsLinux)) {
    Write-Verbose "Ignoring -IncludeX86 switch because 32-bit runtimes are only supported on Windows."
    $IncludeX86 = $false
}

$arch = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture
if (!$arch) { # Windows Powershell leaves this blank
    $arch = 'x64'
    if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { $arch = 'ARM64' }
    if (${env:ProgramFiles(Arm)}) { $arch = 'ARM64' }
}

# Search for all .NET runtime versions referenced from MSBuild projects and arrange to install them.
$runtimeVersions = @()
$windowsDesktopRuntimeVersions = @()
$aspnetRuntimeVersions = @()
if (!$SdkOnly) {
    $projFiles = Get-ChildItem "$PSScriptRoot\..\src\*.*proj", "$PSScriptRoot\..\test\*.*proj" -Recurse
    $projFiles += Get-ChildItem "$PSScriptRoot\..\src\Directory.Build.props", "$PSScriptRoot\..\test\Directory.Build.props" -Recurse
    $projFiles += Get-Item -LiteralPath "$PSScriptRoot\..\Directory.Build.props"
    $projFiles | % {
        $projXml = [xml](Get-Content -LiteralPath $_)
        $pg = $projXml.Project.PropertyGroup
        if ($pg) {
            $targetFrameworks = @()
            $tf = $pg.TargetFramework
            $targetFrameworks += $tf
            $tfs = $pg.TargetFrameworks
            if ($tfs) {
                $targetFrameworks = $tfs -Split ';'
            }
        }
        $targetFrameworks |? { $_ -match 'net(?:coreapp)?(\d+\.\d+)' } |% {
            $v = $Matches[1]
            $runtimeVersions += $v
            $aspnetRuntimeVersions += $v
            if ($v -ge '3.0' -and -not ($IsMacOS -or $IsLinux)) {
                $windowsDesktopRuntimeVersions += $v
            }
        }

        # Add target frameworks of the form: netXX
        $targetFrameworks |? { $_ -match 'net(\d+\.\d+)' } |% {
            $v = $Matches[1]
            $runtimeVersions += $v
            $aspnetRuntimeVersions += $v
            if (-not ($IsMacOS -or $IsLinux)) {
                $windowsDesktopRuntimeVersions += $v
            }
        }
    }
}

if (!$IncludeAspNetCore) {
    $aspnetRuntimeVersions = @()
}

Function Get-FileFromWeb([Uri]$Uri, $OutDir, [switch]$Force) {
    $OutFile = Join-Path $OutDir $Uri.Segments[-1]
    if ($Force -or !(Test-Path $OutFile)) {
        Write-Verbose "Downloading $Uri..."
        if (!(Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }
        try {
            (New-Object System.Net.WebClient).DownloadFile($Uri, $OutFile)
        } finally {
            # This try/finally causes the script to abort
        }
    }

    $OutFile
}

Function Get-InstallerExe(
    $Version,
    $Architecture,
    [ValidateSet('Sdk','Runtime','WindowsDesktop')]
    [string]$sku
) {
    # Get the latest/actual version for the specified one
    $TypedVersion = $null
    if (![Version]::TryParse($Version, [ref] $TypedVersion)) {
        Write-Error "Unable to parse $Version into an a.b.c.d version. This version cannot be installed machine-wide."
        exit 1
    }

    if ($TypedVersion.Build -eq -1) {
        $versionInfo = -Split (Invoke-WebRequest -Uri "https://builds.dotnet.microsoft.com/dotnet/$sku/$Version/latest.version" -UseBasicParsing)
        $Version = $versionInfo[-1]
    }

    $majorMinor = "$($TypedVersion.Major).$($TypedVersion.Minor)"
    $ReleasesFile = Join-Path $DotNetInstallScriptRoot "$majorMinor\releases.json"
    $ReleasesFileWasCached = Test-Path $ReleasesFile
    $ReleasesUri = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/$majorMinor/releases.json"
    if (!$ReleasesFileWasCached) {
        Get-FileFromWeb -Uri $ReleasesUri -OutDir (Split-Path $ReleasesFile) | Out-Null
    }

    $url = $null
    for ($attempt = 0; $attempt -lt 2; $attempt++) {
        $releases = Get-Content $ReleasesFile | ConvertFrom-Json
        foreach ($release in $releases.releases) {
            $filesElement = $null
            if ($release.$sku.version -eq $Version) {
                $filesElement = $release.$sku.files
            }
            if (!$filesElement -and ($sku -eq 'sdk') -and $release.sdks) {
                foreach ($sdk in $release.sdks) {
                    if ($sdk.version -eq $Version) {
                        $filesElement = $sdk.files
                        break
                    }
                }
            }

            if ($filesElement) {
                foreach ($file in $filesElement) {
                    if ($file.rid -eq "win-$Architecture") {
                        $url = $file.url
                        Break
                    }
                }

                if ($url) {
                    Break
                }
            }
        }

        if ($url -or !$ReleasesFileWasCached -or $attempt -gt 0) {
            break
        }

        Write-Verbose "Release not found in cached metadata. Refreshing $ReleasesUri..."
        Get-FileFromWeb -Uri $ReleasesUri -OutDir (Split-Path $ReleasesFile) -Force | Out-Null
    }

    if ($url) {
        Get-FileFromWeb -Uri $url -OutDir $DotNetInstallScriptRoot
    } else {
        throw "Unable to find release of $sku v$Version"
    }
}

Function Install-DotNet($Version, $Architecture, [ValidateSet('Sdk','Runtime','WindowsDesktop','AspNetCore')][string]$sku = 'Sdk') {
    Write-Host "Downloading .NET $sku $Version..."
    $Installer = Get-InstallerExe -Version $Version -Architecture $Architecture -sku $sku
    Write-Host "Installing .NET $sku $Version..."
    cmd /c start /wait $Installer /install /passive /norestart
    if ($LASTEXITCODE -eq 3010) {
        Write-Verbose "Restart required"
    } elseif ($LASTEXITCODE -ne 0) {
        throw "Failure to install .NET SDK"
    }
}

Function Test-DotNetRuntimeInstalled(
    $DotNetRoot,
    [ValidateSet('Microsoft.NETCore.App','Microsoft.WindowsDesktop.App','Microsoft.AspNetCore.App')]
    [string]$Runtime,
    [Version]$Version
) {
    if (!$DotNetRoot) {
        return $false
    }

    $runtimeRoot = Join-Path $DotNetRoot "shared\$Runtime"
    if (!(Test-Path -LiteralPath $runtimeRoot)) {
        return $false
    }

    foreach ($runtimeDirectory in Get-ChildItem -LiteralPath $runtimeRoot -Directory) {
        $installedVersion = $null
        if ([Version]::TryParse($runtimeDirectory.Name, [ref]$installedVersion) -and
            $Version.Major -eq $installedVersion.Major -and
            $Version.Minor -eq $installedVersion.Minor -and
            (($Version.Build -eq -1) -or ($Version.Build -eq $installedVersion.Build)) -and
            (($Version.Revision -eq -1) -or ($Version.Revision -eq $installedVersion.Revision))) {
            return $true
        }
    }

    return $false
}

Function Test-DotNetSdkInstalled($DotNetRoot, $Version, $Channel) {
    if (!$DotNetRoot) {
        return $false
    }

    $sdkRoot = Join-Path $DotNetRoot 'sdk'
    if (!(Test-Path -LiteralPath $sdkRoot)) {
        return $false
    }

    if ($Version) {
        return Test-Path -LiteralPath (Join-Path $sdkRoot $Version) -PathType Container
    }

    $channelVersion = $null
    if (![Version]::TryParse($Channel, [ref]$channelVersion)) {
        return $false
    }

    foreach ($sdkDirectory in Get-ChildItem -LiteralPath $sdkRoot -Directory) {
        $installedVersion = $null
        if ([Version]::TryParse($sdkDirectory.Name, [ref]$installedVersion) -and
            $channelVersion.Major -eq $installedVersion.Major -and
            $channelVersion.Minor -eq $installedVersion.Minor) {
            return $true
        }
    }

    return $false
}

Function Get-MachineDotNetInstallDir([string]$Architecture) {
    # The native Program Files directory, even when this is a 32-bit process on 64-bit Windows.
    $nativeProgramFiles = if ($env:ProgramW6432) { $env:ProgramW6432 } else { $env:ProgramFiles }
    $isArm64Windows = [bool]${env:ProgramFiles(Arm)}
    switch ($Architecture) {
        'x86' {
            if (${env:ProgramFiles(x86)}) { Join-Path ${env:ProgramFiles(x86)} 'dotnet' } else { Join-Path $nativeProgramFiles 'dotnet' }
        }
        'x64' {
            # On ARM64 Windows, the x64 .NET installers use a dotnet\x64 subdirectory.
            if ($isArm64Windows) { Join-Path $nativeProgramFiles 'dotnet\x64' } else { Join-Path $nativeProgramFiles 'dotnet' }
        }
        default {
            Join-Path $nativeProgramFiles 'dotnet'
        }
    }
}

$switches = @()
$envVars = @{
    # For locally installed dotnet, skip first time experience which takes a long time
    'DOTNET_SKIP_FIRST_TIME_EXPERIENCE' = 'true';
}

if ($InstallLocality -eq 'machine') {
    if ($IsMacOS -or $IsLinux) {
        $DotNetInstallDir = '/usr/share/dotnet'
    } else {
        # Check for installed components where the installers for each architecture put them.
        $DotNetInstallDir = Get-MachineDotNetInstallDir -Architecture $arch
        $DotNetX86InstallDir = Get-MachineDotNetInstallDir -Architecture x86
        $restartRequired = $false
        $sdks |% {
            if ($_.Version) { $version = $_.Version } else { $version = $_.Channel }
            if (!(Test-DotNetSdkInstalled -DotNetRoot $DotNetInstallDir -Version $_.Version -Channel $_.Channel) -and
                $PSCmdlet.ShouldProcess(".NET SDK $version ($arch)", "Install")) {
                Install-DotNet -Version $version -Architecture $arch
                $restartRequired = $restartRequired -or ($LASTEXITCODE -eq 3010)
            }

            if ($IncludeX86 -and
                !(Test-DotNetSdkInstalled -DotNetRoot $DotNetX86InstallDir -Version $_.Version -Channel $_.Channel) -and
                $PSCmdlet.ShouldProcess(".NET SDK $version (x86)", "Install")) {
                Install-DotNet -Version $version -Architecture x86
                $restartRequired = $restartRequired -or ($LASTEXITCODE -eq 3010)
            }
        }

        $runtimeVersions | Sort-Object -Unique |% {
            if (!(Test-DotNetRuntimeInstalled -DotNetRoot $DotNetInstallDir -Runtime Microsoft.NETCore.App -Version $_) -and
                $PSCmdlet.ShouldProcess(".NET runtime $_ ($arch)", "Install")) {
                Install-DotNet -Version $_ -sku Runtime -Architecture $arch
                $restartRequired = $restartRequired -or ($LASTEXITCODE -eq 3010)
            }

            if ($IncludeX86 -and
                !(Test-DotNetRuntimeInstalled -DotNetRoot $DotNetX86InstallDir -Runtime Microsoft.NETCore.App -Version $_) -and
                $PSCmdlet.ShouldProcess(".NET runtime $_ (x86)", "Install")) {
                Install-DotNet -Version $_ -sku Runtime -Architecture x86
                $restartRequired = $restartRequired -or ($LASTEXITCODE -eq 3010)
            }
        }

        $windowsDesktopRuntimeVersions | Sort-Object -Unique |% {
            if (!(Test-DotNetRuntimeInstalled -DotNetRoot $DotNetInstallDir -Runtime Microsoft.WindowsDesktop.App -Version $_) -and
                $PSCmdlet.ShouldProcess(".NET Windows Desktop $_ ($arch)", "Install")) {
                Install-DotNet -Version $_ -sku WindowsDesktop -Architecture $arch
                $restartRequired = $restartRequired -or ($LASTEXITCODE -eq 3010)
            }

            if ($IncludeX86 -and
                !(Test-DotNetRuntimeInstalled -DotNetRoot $DotNetX86InstallDir -Runtime Microsoft.WindowsDesktop.App -Version $_) -and
                $PSCmdlet.ShouldProcess(".NET Windows Desktop $_ (x86)", "Install")) {
                Install-DotNet -Version $_ -sku WindowsDesktop -Architecture x86
                $restartRequired = $restartRequired -or ($LASTEXITCODE -eq 3010)
            }
        }

        $aspnetRuntimeVersions | Sort-Object -Unique |% {
            if (!(Test-DotNetRuntimeInstalled -DotNetRoot $DotNetInstallDir -Runtime Microsoft.AspNetCore.App -Version $_) -and
                $PSCmdlet.ShouldProcess("ASP.NET Core $_ ($arch)", "Install")) {
                Install-DotNet -Version $_ -sku AspNetCore -Architecture $arch
                $restartRequired = $restartRequired -or ($LASTEXITCODE -eq 3010)
            }

            if ($IncludeX86 -and
                !(Test-DotNetRuntimeInstalled -DotNetRoot $DotNetX86InstallDir -Runtime Microsoft.AspNetCore.App -Version $_) -and
                $PSCmdlet.ShouldProcess("ASP.NET Core $_ (x86)", "Install")) {
                Install-DotNet -Version $_ -sku AspNetCore -Architecture x86
                $restartRequired = $restartRequired -or ($LASTEXITCODE -eq 3010)
            }
        }
        if ($restartRequired) {
            Write-Host -ForegroundColor Yellow "System restart required"
            Exit 3010
        }

        return
    }
} elseif ($InstallLocality -eq 'repo') {
    $DotNetInstallDir = "$DotNetInstallScriptRoot/.dotnet"
    $DotNetX86InstallDir = "$DotNetInstallScriptRoot/x86/.dotnet"
} elseif ($env:AGENT_TOOLSDIRECTORY) {
    $DotNetInstallDir = "$env:AGENT_TOOLSDIRECTORY/dotnet"
    $DotNetX86InstallDir = "$env:AGENT_TOOLSDIRECTORY/x86/dotnet"
} else {
    $DotNetInstallDir = Join-Path $HOME .dotnet
}

if ($DotNetInstallDir) {
    if (!(Test-Path $DotNetInstallDir)) { New-Item -ItemType Directory -Path $DotNetInstallDir }
    $DotNetInstallDir = Resolve-Path $DotNetInstallDir
    Write-Host "Installing .NET SDK and runtimes to $DotNetInstallDir" -ForegroundColor Blue
    $envVars['DOTNET_MULTILEVEL_LOOKUP'] = '0'
    $envVars['DOTNET_ROOT'] = $DotNetInstallDir
}

if ($IncludeX86) {
    if ($DotNetX86InstallDir) {
        if (!(Test-Path $DotNetX86InstallDir)) { New-Item -ItemType Directory -Path $DotNetX86InstallDir }
        $DotNetX86InstallDir = Resolve-Path $DotNetX86InstallDir
        Write-Host "Installing x86 .NET SDK and runtimes to $DotNetX86InstallDir" -ForegroundColor Blue
    } else {
        # Only machine-wide or repo-wide installations can handle two unique dotnet.exe architectures.
        Write-Error "The installation location or OS isn't supported for x86 installation. Try a different -InstallLocality value."
        return 1
    }
}

if ($IsMacOS -or $IsLinux) {
    $DotNetInstallScriptPath = "$PSScriptRoot/dotnet-install.sh"
} else {
    $DotNetInstallScriptPath = "$PSScriptRoot/dotnet-install.ps1"
}

# Verify the cached script exists
if (-not (Test-Path $DotNetInstallScriptPath)) {
    Write-Error "Cached dotnet-install script not found at $DotNetInstallScriptPath. Run tools/Update-DotNetInstallScript.ps1 to download it."
    exit 1
}

# In case the script we invoke is in a directory with spaces, wrap it with single quotes.
# In case the path includes single quotes, escape them.
$DotNetInstallScriptPathExpression = $DotNetInstallScriptPath.Replace("'", "''")
$DotNetInstallScriptPathExpression = "& '$DotNetInstallScriptPathExpression'"

$anythingInstalled = $false
$global:LASTEXITCODE = 0

$sdks |% {
    if ($_.Version) { $parameters = '-Version', $_.Version } else { $parameters = '-Channel', $_.Channel }

    if (!(Test-DotNetSdkInstalled -DotNetRoot $DotNetInstallDir -Version $_.Version -Channel $_.Channel)) {
        if ($PSCmdlet.ShouldProcess(".NET SDK $_ ($arch)", "Install")) {
            $anythingInstalled = $true
            Invoke-Expression -Command "$DotNetInstallScriptPathExpression $parameters -Architecture $arch -InstallDir $DotNetInstallDir $switches"

            if ($LASTEXITCODE -ne 0) {
                Write-Error ".NET SDK installation failure: $LASTEXITCODE"
                exit $LASTEXITCODE
            }
        } else {
            Invoke-Expression -Command "$DotNetInstallScriptPathExpression $parameters -Architecture $arch -InstallDir $DotNetInstallDir $switches -DryRun"
        }
    }

    if ($IncludeX86 -and !(Test-DotNetSdkInstalled -DotNetRoot $DotNetX86InstallDir -Version $_.Version -Channel $_.Channel)) {
        if ($PSCmdlet.ShouldProcess(".NET x86 SDK $_", "Install")) {
            $anythingInstalled = $true
            Invoke-Expression -Command "$DotNetInstallScriptPathExpression $parameters -Architecture x86 -InstallDir $DotNetX86InstallDir $switches"

            if ($LASTEXITCODE -ne 0) {
                Write-Error ".NET x86 SDK installation failure: $LASTEXITCODE"
                exit $LASTEXITCODE
            }
        } else {
            Invoke-Expression -Command "$DotNetInstallScriptPathExpression $parameters -Architecture x86 -InstallDir $DotNetX86InstallDir $switches -DryRun"
        }
    }
}

$dotnetRuntimeSwitches = $switches + '-Runtime','dotnet'

$runtimeVersions | Sort-Object -Unique |% {
    if (!(Test-DotNetRuntimeInstalled -DotNetRoot $DotNetInstallDir -Runtime Microsoft.NETCore.App -Version $_)) {
        if ($PSCmdlet.ShouldProcess(".NET $Arch runtime $_", "Install")) {
            $anythingInstalled = $true
            Invoke-Expression -Command "$DotNetInstallScriptPathExpression -Channel $_ -Architecture $arch -InstallDir $DotNetInstallDir $dotnetRuntimeSwitches"

            if ($LASTEXITCODE -ne 0) {
                Write-Error ".NET SDK installation failure: $LASTEXITCODE"
                exit $LASTEXITCODE
            }
        } else {
            Invoke-Expression -Command "$DotNetInstallScriptPathExpression -Channel $_ -Architecture $arch -InstallDir $DotNetInstallDir $dotnetRuntimeSwitches -DryRun"
        }
    }

    if ($IncludeX86 -and !(Test-DotNetRuntimeInstalled -DotNetRoot $DotNetX86InstallDir -Runtime Microsoft.NETCore.App -Version $_)) {
        if ($PSCmdlet.ShouldProcess(".NET x86 runtime $_", "Install")) {
            $anythingInstalled = $true
            Invoke-Expression -Command "$DotNetInstallScriptPathExpression -Channel $_ -Architecture x86 -InstallDir $DotNetX86InstallDir $dotnetRuntimeSwitches"

            if ($LASTEXITCODE -ne 0) {
                Write-Error ".NET SDK installation failure: $LASTEXITCODE"
                exit $LASTEXITCODE
            }
        } else {
            Invoke-Expression -Command "$DotNetInstallScriptPathExpression -Channel $_ -Architecture x86 -InstallDir $DotNetX86InstallDir $dotnetRuntimeSwitches -DryRun"
        }
    }
}

$windowsDesktopRuntimeSwitches = $switches + '-Runtime','windowsdesktop'

$windowsDesktopRuntimeVersions | Sort-Object -Unique |% {
    if (!(Test-DotNetRuntimeInstalled -DotNetRoot $DotNetInstallDir -Runtime Microsoft.WindowsDesktop.App -Version $_)) {
        if ($PSCmdlet.ShouldProcess(".NET WindowsDesktop $arch runtime $_", "Install")) {
            $anythingInstalled = $true
            Invoke-Expression -Command "$DotNetInstallScriptPathExpression -Channel $_ -Architecture $arch -InstallDir $DotNetInstallDir $windowsDesktopRuntimeSwitches"

            if ($LASTEXITCODE -ne 0) {
                Write-Error ".NET SDK installation failure: $LASTEXITCODE"
                exit $LASTEXITCODE
            }
        } else {
            Invoke-Expression -Command "$DotNetInstallScriptPathExpression -Channel $_ -Architecture $arch -InstallDir $DotNetInstallDir $windowsDesktopRuntimeSwitches -DryRun"
        }
    }

    if ($IncludeX86 -and !(Test-DotNetRuntimeInstalled -DotNetRoot $DotNetX86InstallDir -Runtime Microsoft.WindowsDesktop.App -Version $_)) {
        if ($PSCmdlet.ShouldProcess(".NET WindowsDesktop x86 runtime $_", "Install")) {
            $anythingInstalled = $true
            Invoke-Expression -Command "$DotNetInstallScriptPathExpression -Channel $_ -Architecture x86 -InstallDir $DotNetX86InstallDir $windowsDesktopRuntimeSwitches"

            if ($LASTEXITCODE -ne 0) {
                Write-Error ".NET SDK installation failure: $LASTEXITCODE"
                exit $LASTEXITCODE
            }
        } else {
            Invoke-Expression -Command "$DotNetInstallScriptPathExpression -Channel $_ -Architecture x86 -InstallDir $DotNetX86InstallDir $windowsDesktopRuntimeSwitches -DryRun"
        }
    }
}

$aspnetRuntimeSwitches = $switches + '-Runtime','aspnetcore'

$aspnetRuntimeVersions | Sort-Object -Unique |% {
    if (!(Test-DotNetRuntimeInstalled -DotNetRoot $DotNetInstallDir -Runtime Microsoft.AspNetCore.App -Version $_)) {
        if ($PSCmdlet.ShouldProcess(".NET ASP.NET Core $arch runtime $_", "Install")) {
            $anythingInstalled = $true
            Invoke-Expression -Command "$DotNetInstallScriptPathExpression -Channel $_ -Architecture $arch -InstallDir $DotNetInstallDir $aspnetRuntimeSwitches"

            if ($LASTEXITCODE -ne 0) {
                Write-Error ".NET SDK installation failure: $LASTEXITCODE"
                exit $LASTEXITCODE
            }
        } else {
            Invoke-Expression -Command "$DotNetInstallScriptPathExpression -Channel $_ -Architecture $arch -InstallDir $DotNetInstallDir $aspnetRuntimeSwitches -DryRun"
        }
    }

    if ($IncludeX86 -and !(Test-DotNetRuntimeInstalled -DotNetRoot $DotNetX86InstallDir -Runtime Microsoft.AspNetCore.App -Version $_)) {
        if ($PSCmdlet.ShouldProcess(".NET ASP.NET Core x86 runtime $_", "Install")) {
            $anythingInstalled = $true
            Invoke-Expression -Command "$DotNetInstallScriptPathExpression -Channel $_ -Architecture x86 -InstallDir $DotNetX86InstallDir $aspnetRuntimeSwitches"

            if ($LASTEXITCODE -ne 0) {
                Write-Error ".NET SDK installation failure: $LASTEXITCODE"
                exit $LASTEXITCODE
            }
        } else {
            Invoke-Expression -Command "$DotNetInstallScriptPathExpression -Channel $_ -Architecture x86 -InstallDir $DotNetX86InstallDir $aspnetRuntimeSwitches -DryRun"
        }
    }
}

if ($PSCmdlet.ShouldProcess("Set DOTNET environment variables to discover these installed runtimes?")) {
    & "$PSScriptRoot/Set-EnvVars.ps1" -Variables $envVars -PrependPath $DotNetInstallDir | Out-Null
}

if ($anythingInstalled -and ($InstallLocality -ne 'machine') -and !$env:TF_BUILD -and !$env:GITHUB_ACTIONS) {
    Write-Warning ".NET runtimes or SDKs were installed to a non-machine location. Perform your builds or open Visual Studio from this same environment in order for tools to discover the location of these dependencies."
}
