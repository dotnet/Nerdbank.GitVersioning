<#
.SYNOPSIS
Generates a version.txt file if one does not exist.
.DESCRIPTION
When creating version.txt, AssemblyInfo.cs is loaded and the Major.Minor from the AssemblyVersion attribute is
used to seed the version number. Then, those Assembly attributes are removed from AssemblyInfo.cs. This cmdlet
returns the path to the generated file, or null if the file was not created (it already existed, or the cmdlet
was being executed with -WhatIf).
.PARAMETER ProjectDirectory
The directory of the project which is adding versioning logic with Nerdbank.GitVersioning.
.PARAMETER OutputDirectory
The directory where version.txt should be generated. Defaults to the project directory if not specified.
This should either be the project directory, or in a parent directory of the project inside the repo.
#>
[CmdletBinding(SupportsShouldProcess)]
Param(
    [Parameter()]
    [string]$ProjectDirectory=".",
    [Parameter()]
    [string]$OutputDirectory=$null
)

$ProjectDirectory = Resolve-Path $ProjectDirectory
if (!$OutputDirectory)
{
    $OutputDirectory = $ProjectDirectory
}

$versionTxtPath = Join-Path $OutputDirectory "version.txt"

if (-not (Test-Path $versionTxtPath))
{
    # The version file doesn't exist, which means this package is being installed for the first time.
    # 1) Load up the AssemblyInfo.cs file and grab the existing version declarations.
    # 2) Generate the version.txt with the version seeded from AssemblyInfo.cs
    # 3) Delete the version-related attributes in AssemblyInfo.cs

    $propertiesDirectory = Join-Path $ProjectDirectory "Properties"
    $assemblyInfo = Join-Path $propertiesDirectory "AssemblyInfo.cs"
    $version = $null
    if (Test-Path $assemblyInfo)
    {
        $fixedLines = (Get-Content $assemblyInfo) | ForEach-Object {
            if ($_ -match "^\w*\[assembly: AssemblyVersion\(""([0-9]+.[0-9]+|\*)(?:.(?:[0-9]+|\*)){0,2}""\)\]$")
            {
                # Grab the Major.Minor out of this file which will be injected into the version.txt
                $version = $matches[1] + ".0"
            }

            # Remove attributes related to assembly versioning since those are generated on the fly during the build
            $_ -replace "^\[assembly: Assembly(?:File|Informational|)Version\(""[0-9]+(?:.(?:[0-9]+|\*)){1,3}""\)\]$"
        }

        if ($PSCmdlet.ShouldProcess($assemblyInfo, "Removing assembly attributes"))
        {
            $fixedLines | Set-Content $assemblyInfo -Encoding UTF8
        }
    }
    
    if (!$version)
    {
        $version = "1.0.0"
        Write-Warning "Could not parse the AssemblyVersion for this project. Generating version.txt with the default version of '$version'"
    }

    if ($PSCmdlet.ShouldProcess($versionTxtPath, "Writing version.txt file"))
    {
        $version | Set-Content $versionTxtPath
        $versionTxtPath
    }
}
