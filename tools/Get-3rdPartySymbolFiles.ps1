[CmdletBinding()]
Param (
)

# Symbol servers to search for PDBs, in order of priority.
$SymbolServers = @(
    'https://msdl.microsoft.com/download/symbols'
    'https://symbols.nuget.org/download/symbols'
)

Function Get-SymbolsFromPackage($id, $version) {
    $symbolPackagesPath = "$PSScriptRoot/../obj/SymbolsPackages"
    New-Item -ItemType Directory -Path $symbolPackagesPath -Force | Out-Null
    $packagePath = $null

    # Download the package from configured feeds (failures are non-fatal for symbol collection)
    $previousLastExitCode = $global:LASTEXITCODE
    try {
        $packagePath = & "$PSScriptRoot\Download-NuGetPackage.ps1" -PackageId $id -Version $version -OutputDirectory $symbolPackagesPath -ErrorAction SilentlyContinue
    }
    catch {
        Write-Warning "Failed to download package $id $version from configured feeds. Skipping if not found locally. $($_.Exception.Message)"
    }
    $global:LASTEXITCODE = $previousLastExitCode
    if (!$packagePath -or !(Test-Path -LiteralPath $packagePath)) {
        Write-Warning "Package $id $version not found in configured feeds. Skipping."
        return
    }

    # Download symbols for each binary using dotnet-symbol
    $serverArgs = $SymbolServers | ForEach-Object { '--server-path'; $_ }
    $binaries = @(Get-ChildItem -Recurse -LiteralPath $packagePath -Include *.dll, *.exe)
    if ($binaries) {
        $prevErrorActionPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        & dotnet symbol --symbols @serverArgs @($binaries.FullName) 2>&1 | Out-Null
        $ErrorActionPreference = $prevErrorActionPreference
    }

    # Output pairs of binary + PDB paths for archival
    Get-ChildItem -Recurse -LiteralPath $packagePath -Filter *.pdb | % {
        $rootName = Join-Path $_.Directory $_.BaseName
        if ($rootName.EndsWith('.ni')) {
            $rootName = $rootName.Substring(0, $rootName.Length - 3)
        }

        $dllPath = "$rootName.dll"
        $exePath = "$rootName.exe"
        if (Test-Path $dllPath) {
            $BinaryImagePath = $dllPath
        }
        elseif (Test-Path $exePath) {
            $BinaryImagePath = $exePath
        }
        else {
            Write-Warning "`"$_`" found with no matching binary file."
            $BinaryImagePath = $null
        }

        if ($BinaryImagePath) {
            Write-Output $BinaryImagePath
            Write-Output $_.FullName
        }
    }
}

Function Get-PackageVersions() {
    if ($script:PackageVersions) {
        return $script:PackageVersions
    }

    $propsPath = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\Directory.Packages.props')).Path
    $output = & dotnet msbuild $propsPath -nologo -verbosity:quiet -getItem:PackageVersion 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to evaluate package versions from Directory.Packages.props.`n$($output | Out-String)"
        return @{}
    }

    $jsonText = ($output | Out-String).Trim()
    $jsonStart = $jsonText.IndexOf('{')
    if ($jsonStart -lt 0) {
        Write-Error 'Failed to locate JSON output from `dotnet msbuild -getItem:PackageVersion`.'
        return @{}
    }

    $packageVersions = @{}
    foreach ($item in @((ConvertFrom-Json $jsonText.Substring($jsonStart)).Items.PackageVersion)) {
        $packageVersions[$item.Identity] = $item.Version
    }

    $script:PackageVersions = $packageVersions
    $packageVersions
}

Function Get-PackageVersion($id) {
    $version = (Get-PackageVersions)[$id]
    if (!$version) {
        Write-Error "No package version found in Directory.Packages.props for the package '$id'"
    }

    $version
}

# All 3rd party packages for which symbols packages are expected should be listed here.
# Packages are downloaded from configured feeds in nuget.config.
# We should NOT add 3rd party packages to this list because PDBs may be unsafe for our debuggers to load,
# so we should only archive 1st party symbols.
$3rdPartyPackageIds = @()

$3rdPartyPackageIds | % {
    $version = Get-PackageVersion $_
    if ($version) {
        Write-Verbose "Downloading symbols for package '$_' version '$version'."
        Get-SymbolsFromPackage -id $_ -version $version
    } else {
        Write-Warning "No version found for package '$_'. Skipping symbol download."
    }
}
