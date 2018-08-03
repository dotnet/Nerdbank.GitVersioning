# VersionOptions Class
> Describes the various versions and options required for the build.

**Namespace:** Nerdbank.GitVersioning

**Assembly:** NerdBank.GitVersioning (in NerdBank.GitVersioning.dll)
## Inheritance Hierarchy
&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;System.Object

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Nerdbank.GitVersioning.VersionOptions

## Syntax
~~~~csharp
[System.Diagnostics.DebuggerDisplay("
~~~~
## Properties
|Name|Description|
|---|---|
|[Version](/doc/Nerdbank/GitVersioning/VersionOptions/Properties/Version.md)|Gets or sets the default version to use.|
|[AssemblyVersion](/doc/Nerdbank/GitVersioning/VersionOptions/Properties/AssemblyVersion.md)|Gets or sets the version to use particularly for the 
            instead of the default .|
|[AssemblyVersionOrDefault](/doc/Nerdbank/GitVersioning/VersionOptions/Properties/AssemblyVersionOrDefault.md)|Gets the version to use particularly for the 
            instead of the default .|
|[BuildNumberOffset](/doc/Nerdbank/GitVersioning/VersionOptions/Properties/BuildNumberOffset.md)|Gets or sets a number to add to the git height when calculating the  number.|
|[BuildNumberOffsetOrDefault](/doc/Nerdbank/GitVersioning/VersionOptions/Properties/BuildNumberOffsetOrDefault.md)|Gets a number to add to the git height when calculating the  number.|
|[SemVer1NumericIdentifierPadding](/doc/Nerdbank/GitVersioning/VersionOptions/Properties/SemVer1NumericIdentifierPadding.md)|Gets or sets the minimum number of digits to use for numeric identifiers in SemVer 1.|
|[SemVer1NumericIdentifierPaddingOrDefault](/doc/Nerdbank/GitVersioning/VersionOptions/Properties/SemVer1NumericIdentifierPaddingOrDefault.md)|Gets the minimum number of digits to use for numeric identifiers in SemVer 1.|
|[NuGetPackageVersion](/doc/Nerdbank/GitVersioning/VersionOptions/Properties/NuGetPackageVersion.md)|Gets or sets the options around NuGet version strings|
|[NuGetPackageVersionOrDefault](/doc/Nerdbank/GitVersioning/VersionOptions/Properties/NuGetPackageVersionOrDefault.md)|Gets the options around NuGet version strings|
|[PublicReleaseRefSpec](/doc/Nerdbank/GitVersioning/VersionOptions/Properties/PublicReleaseRefSpec.md)|Gets or sets an array of regular expressions that describes branch or tag names that should
            be built with PublicRelease=true as the default value on build servers.|
|[PublicReleaseRefSpecOrDefault](/doc/Nerdbank/GitVersioning/VersionOptions/Properties/PublicReleaseRefSpecOrDefault.md)|Gets an array of regular expressions that describes branch or tag names that should
            be built with PublicRelease=true as the default value on build servers.|
|[CloudBuild](/doc/Nerdbank/GitVersioning/VersionOptions/Properties/CloudBuild.md)|Gets or sets the options around cloud build.|
|[CloudBuildOrDefault](/doc/Nerdbank/GitVersioning/VersionOptions/Properties/CloudBuildOrDefault.md)|Gets the options around cloud build.|
|[Inherit](/doc/Nerdbank/GitVersioning/VersionOptions/Properties/Inherit.md)|Gets or sets a value indicating whether this options object should inherit from an ancestor any settings that are not explicitly set in this one.|
## Methods
|Name|Description|
|---|---|
|[FromVersion(Version, String)](/doc/Nerdbank/GitVersioning/VersionOptions/Methods/FromVersion_Version%2c%20String_.md)|Initializes a new instance of the  class
            with  initialized with the specified parameters.|
|[GetJsonSettings(Boolean)](/doc/Nerdbank/GitVersioning/VersionOptions/Methods/GetJsonSettings_Boolean_.md)|Gets the  to use based on certain requirements.|
|[Equals(Object)](/doc/Nerdbank/GitVersioning/VersionOptions/Methods/Equals_Object_.md)|Checks equality against another object.|
|[Equals(VersionOptions)](/doc/Nerdbank/GitVersioning/VersionOptions/Methods/Equals_VersionOptions_.md)|Checks equality against another instance of this class.|
## Fields
|Name|Description|
|---|---|
|[DefaultVersionPrecision](/doc/Nerdbank/GitVersioning/VersionOptions/Fields/DefaultVersionPrecision.md)|Default value for .|
