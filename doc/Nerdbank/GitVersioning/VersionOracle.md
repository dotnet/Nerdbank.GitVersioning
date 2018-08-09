# VersionOracle Class
> Assembles version information in a variety of formats.

**Namespace:** Nerdbank.GitVersioning

**Assembly:** NerdBank.GitVersioning (in NerdBank.GitVersioning.dll)
## Inheritance Hierarchy
&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;System.Object

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Nerdbank.GitVersioning.VersionOracle

## Syntax
~~~~csharp
public class VersionOracle
~~~~
## Properties
|Name|Description|
|---|---|
|[CloudBuildNumber](/doc/Nerdbank/GitVersioning/VersionOracle/Properties/CloudBuildNumber.md)|Gets the BuildNumber to set the cloud build to (if applicable).|
|[CloudBuildNumberEnabled](/doc/Nerdbank/GitVersioning/VersionOracle/Properties/CloudBuildNumberEnabled.md)|Gets a value indicating whether the cloud build number should be set.|
|[BuildMetadataWithCommitId](/doc/Nerdbank/GitVersioning/VersionOracle/Properties/BuildMetadataWithCommitId.md)|Gets the build metadata identifiers, including the git commit ID as the first identifier if appropriate.|
|[VersionFileFound](/doc/Nerdbank/GitVersioning/VersionOracle/Properties/VersionFileFound.md)|Gets a value indicating whether a version.json or version.txt file was found.|
|[AssemblyVersion](/doc/Nerdbank/GitVersioning/VersionOracle/Properties/AssemblyVersion.md)|Gets the version string to use for the .|
|[AssemblyFileVersion](/doc/Nerdbank/GitVersioning/VersionOracle/Properties/AssemblyFileVersion.md)|Gets the version string to use for the .|
|[AssemblyInformationalVersion](/doc/Nerdbank/GitVersioning/VersionOracle/Properties/AssemblyInformationalVersion.md)|Gets the version string to use for the .|
|[PublicRelease](/doc/Nerdbank/GitVersioning/VersionOracle/Properties/PublicRelease.md)|Gets or sets a value indicating whether the project is building in PublicRelease mode.|
|[PrereleaseVersion](/doc/Nerdbank/GitVersioning/VersionOracle/Properties/PrereleaseVersion.md)|Gets the prerelease version information.|
|[SimpleVersion](/doc/Nerdbank/GitVersioning/VersionOracle/Properties/SimpleVersion.md)|Gets the version information without a Revision component.|
|[BuildNumber](/doc/Nerdbank/GitVersioning/VersionOracle/Properties/BuildNumber.md)|Gets the build number (i.e. third integer, or PATCH) for this version.|
|[MajorMinorVersion](/doc/Nerdbank/GitVersioning/VersionOracle/Properties/MajorMinorVersion.md)|Gets or sets the major.minor version string.|
|[GitCommitId](/doc/Nerdbank/GitVersioning/VersionOracle/Properties/GitCommitId.md)|Gets the Git revision control commit id for HEAD (the current source code version).|
|[GitCommitIdShort](/doc/Nerdbank/GitVersioning/VersionOracle/Properties/GitCommitIdShort.md)|Gets the first several characters of the Git revision control commit id for HEAD (the current source code version).|
|[VersionHeight](/doc/Nerdbank/GitVersioning/VersionOracle/Properties/VersionHeight.md)|Gets the number of commits in the longest single path between the specified commit and the most distant ancestor (inclusive) that set the version to the value at HEAD.|
|[VersionHeightOffset](/doc/Nerdbank/GitVersioning/VersionOracle/Properties/VersionHeightOffset.md)|The offset to add to the  when calculating the integer to use as the  or elsewhere that the {height} macro is used.|
|[Version](/doc/Nerdbank/GitVersioning/VersionOracle/Properties/Version.md)|Gets the version for this project, with up to 4 components.|
|[CloudBuildAllVarsEnabled](/doc/Nerdbank/GitVersioning/VersionOracle/Properties/CloudBuildAllVarsEnabled.md)|Gets a value indicating whether to set all cloud build variables prefaced with "NBGV_".|
|[CloudBuildAllVars](/doc/Nerdbank/GitVersioning/VersionOracle/Properties/CloudBuildAllVars.md)|Gets a dictionary of all cloud build variables that applies to this project, regardless of the current setting of .|
|[CloudBuildVersionVarsEnabled](/doc/Nerdbank/GitVersioning/VersionOracle/Properties/CloudBuildVersionVarsEnabled.md)|Gets a value indicating whether to set cloud build version variables.|
|[CloudBuildVersionVars](/doc/Nerdbank/GitVersioning/VersionOracle/Properties/CloudBuildVersionVars.md)|Gets a dictionary of cloud build variables that applies to this project, regardless of the current setting of .|
|[BuildMetadata](/doc/Nerdbank/GitVersioning/VersionOracle/Properties/BuildMetadata.md)|Gets the list of build metadata identifiers to include in semver version strings.|
|[BuildMetadataFragment](/doc/Nerdbank/GitVersioning/VersionOracle/Properties/BuildMetadataFragment.md)|Gets the +buildMetadata fragment for the semantic version.|
|[NuGetPackageVersion](/doc/Nerdbank/GitVersioning/VersionOracle/Properties/NuGetPackageVersion.md)|Gets the version to use for NuGet packages.|
|[NpmPackageVersion](/doc/Nerdbank/GitVersioning/VersionOracle/Properties/NpmPackageVersion.md)|Gets the version to use for NPM packages.|
|[SemVer1](/doc/Nerdbank/GitVersioning/VersionOracle/Properties/SemVer1.md)|Gets a SemVer 1.0 compliant string that represents this version, including the -gCOMMITID suffix when  is false.|
|[SemVer2](/doc/Nerdbank/GitVersioning/VersionOracle/Properties/SemVer2.md)|Gets a SemVer 2.0 compliant string that represents this version, including a +gCOMMITID suffix when  is false.|
|[SemVer1NumericIdentifierPadding](/doc/Nerdbank/GitVersioning/VersionOracle/Properties/SemVer1NumericIdentifierPadding.md)|Gets the minimum number of digits to use for numeric identifiers in SemVer 1.|
