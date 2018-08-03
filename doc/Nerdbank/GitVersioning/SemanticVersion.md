# SemanticVersion Class
> Describes a version with an optional unstable tag.

**Namespace:** Nerdbank.GitVersioning

**Assembly:** NerdBank.GitVersioning (in NerdBank.GitVersioning.dll)
## Inheritance Hierarchy
&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;System.Object

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Nerdbank.GitVersioning.SemanticVersion

## Syntax
~~~~csharp
[System.Diagnostics.DebuggerDisplay("
~~~~
## Constructors
|Name|Description|
|---|---|
|[.ctor(Version, String, String)](/doc/Nerdbank/GitVersioning/SemanticVersion/Constructors/.ctor_Version%2c%20String%2c%20String_.md)|Initializes a new instance of the  class.|
|[.ctor(String, String, String)](/doc/Nerdbank/GitVersioning/SemanticVersion/Constructors/.ctor_String%2c%20String%2c%20String_.md)|Initializes a new instance of the  class.|
## Properties
|Name|Description|
|---|---|
|[Version](/doc/Nerdbank/GitVersioning/SemanticVersion/Properties/Version.md)|Gets the version.|
|[Prerelease](/doc/Nerdbank/GitVersioning/SemanticVersion/Properties/Prerelease.md)|Gets an unstable tag (with the leading hyphen), if applicable.|
|[BuildMetadata](/doc/Nerdbank/GitVersioning/SemanticVersion/Properties/BuildMetadata.md)|Gets the build metadata (with the leading plus), if applicable.|
## Methods
|Name|Description|
|---|---|
|[Parse(String)](/doc/Nerdbank/GitVersioning/SemanticVersion/Methods/Parse_String_.md)|Parses a semantic version from the given string.|
|[Equals(Object)](/doc/Nerdbank/GitVersioning/SemanticVersion/Methods/Equals_Object_.md)|Checks equality against another object.|
|[Equals(SemanticVersion)](/doc/Nerdbank/GitVersioning/SemanticVersion/Methods/Equals_SemanticVersion_.md)|Checks equality against another instance of this class.|
