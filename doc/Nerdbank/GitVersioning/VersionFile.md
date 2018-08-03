# VersionFile Class
> Extension methods for interacting with the version.txt file.

**Namespace:** Nerdbank.GitVersioning

**Assembly:** NerdBank.GitVersioning (in NerdBank.GitVersioning.dll)
## Inheritance Hierarchy
&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;System.Object

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Nerdbank.GitVersioning.VersionFile

## Syntax
~~~~csharp
public static class VersionFile
~~~~
## Methods
|Name|Description|
|---|---|
|[GetVersion(Commit, String)](/doc/Nerdbank/GitVersioning/VersionFile/Methods/GetVersion_Commit%2c%20String_.md)|Reads the version.txt file and returns the  and prerelease tag from it.|
|[GetVersion(Repository, String)](/doc/Nerdbank/GitVersioning/VersionFile/Methods/GetVersion_Repository%2c%20String_.md)|Reads the version.txt file and returns the  and prerelease tag from it.|
|[GetVersion(String)](/doc/Nerdbank/GitVersioning/VersionFile/Methods/GetVersion_String_.md)|Reads the version.txt file and returns the  and prerelease tag from it.|
|[IsVersionDefined(Commit, String)](/doc/Nerdbank/GitVersioning/VersionFile/Methods/IsVersionDefined_Commit%2c%20String_.md)|Checks whether the version.txt file is defined in the specified commit.|
|[IsVersionDefined(String)](/doc/Nerdbank/GitVersioning/VersionFile/Methods/IsVersionDefined_String_.md)|Checks whether the version.txt file is defined in the specified project directory
            or one of its ancestors.|
|[SetVersion(String, VersionOptions)](/doc/Nerdbank/GitVersioning/VersionFile/Methods/SetVersion_String%2c%20VersionOptions_.md)|Writes the version.json file to a directory within a repo with the specified version information.|
|[SetVersion(String, Version, String)](/doc/Nerdbank/GitVersioning/VersionFile/Methods/SetVersion_String%2c%20Version%2c%20String_.md)|Writes the version.txt file to a directory within a repo with the specified version information.|
## Fields
|Name|Description|
|---|---|
|[TxtFileName](/doc/Nerdbank/GitVersioning/VersionFile/Fields/TxtFileName.md)|The filename of the version.txt file.|
|[JsonFileName](/doc/Nerdbank/GitVersioning/VersionFile/Fields/JsonFileName.md)|The filename of the version.json file.|
|[PreferredFileNames](/doc/Nerdbank/GitVersioning/VersionFile/Fields/PreferredFileNames.md)|A sequence of possible filenames for the version file in preferred order.|
