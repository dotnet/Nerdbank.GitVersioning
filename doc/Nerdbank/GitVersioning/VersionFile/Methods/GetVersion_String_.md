# VersionFile.GetVersion(String) Method
> Reads the version.txt file and returns the  and prerelease tag from it.

**Namespace:** Nerdbank.GitVersioning

**Assembly:** NerdBank.GitVersioning (in NerdBank.GitVersioning.dll)
## Syntax
~~~~csharp
public static Nerdbank.GitVersioning.VersionOptions GetVersion(string projectDirectory);
~~~~
##### Parameters
*projectDirectory*

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Type: String

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;The path to the directory which may (or its ancestors may) define the version.txt file.


##### Return Value
Type: VersionOptions

The version information read from the file, or null if the file wasn't found.

