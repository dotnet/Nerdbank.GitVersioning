# Symbol servers to search for PDBs, in order of priority.
$SymbolServers = @(
    'https://msdl.microsoft.com/download/symbols'
    'https://symbols.nuget.org/download/symbols'
)

Function Unzip($Path, $OutDir) {
    $OutDir = (New-Item -ItemType Directory -Path $OutDir -Force).FullName
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    # Start by extracting to a temporary directory so that there are no file conflicts.
    [System.IO.Compression.ZipFile]::ExtractToDirectory($Path, "$OutDir.out")

    # Now move all files from the temp directory to $OutDir, overwriting any files.
    Get-ChildItem -Path "$OutDir.out" -Recurse -File | ForEach-Object {
        $destinationPath = Join-Path -Path $OutDir -ChildPath $_.FullName.Substring("$OutDir.out".Length).TrimStart([io.path]::DirectorySeparatorChar, [io.path]::AltDirectorySeparatorChar)
        if (!(Test-Path -Path (Split-Path -Path $destinationPath -Parent))) {
            New-Item -ItemType Directory -Path (Split-Path -Path $destinationPath -Parent) | Out-Null
        }
        Move-Item -Path $_.FullName -Destination $destinationPath -Force
    }
    Remove-Item -Path "$OutDir.out" -Recurse -Force
}

Function Get-SymbolsFromPackage($id, $version) {
    $symbolPackagesPath = "$PSScriptRoot/../obj/SymbolsPackages"
    New-Item -ItemType Directory -Path $symbolPackagesPath -Force | Out-Null
    $unzippedPkgPath = Join-Path $symbolPackagesPath "$id.$version"

    # Download the package from configured feeds (failures are non-fatal for symbol collection)
    & "$PSScriptRoot\Download-NuGetPackage.ps1" -PackageId $id -Version $version -OutputDirectory $symbolPackagesPath -ErrorAction SilentlyContinue | Out-Null
    $global:LASTEXITCODE = 0
    $nupkgFile = Get-ChildItem -Recurse -Path $symbolPackagesPath -Filter "$id.$version.nupkg" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (!$nupkgFile) {
        Write-Warning "Package $id $version not found in configured feeds. Skipping."
        return
    }

    Unzip -Path $nupkgFile.FullName -OutDir $unzippedPkgPath

    # Download symbols for each binary using dotnet-symbol
    $serverArgs = $SymbolServers | ForEach-Object { '--server-path'; $_ }
    $binaries = Get-ChildItem -Recurse -LiteralPath $unzippedPkgPath -Include *.dll, *.exe
    foreach ($binary in $binaries) {
        $prevErrorActionPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        & dotnet symbol --symbols @serverArgs --output $binary.DirectoryName $binary.FullName 2>&1 | Out-Null
        $ErrorActionPreference = $prevErrorActionPreference
    }

    # Output pairs of binary + PDB paths for archival
    Get-ChildItem -Recurse -LiteralPath $unzippedPkgPath -Filter *.pdb | % {
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

Function Get-PackageVersion($id) {
    $versionProps = [xml](Get-Content -LiteralPath $PSScriptRoot\..\Directory.Packages.props)
    $version = $versionProps.Project.ItemGroup.PackageVersion | ? { $_.Include -eq $id } | % { $_.Version }
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
        Get-SymbolsFromPackage -id $_ -version $version
    }
}
