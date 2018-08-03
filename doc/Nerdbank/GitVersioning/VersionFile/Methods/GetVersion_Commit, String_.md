# VersionFile.GetVersion(Commit, String) Method
> Reads the version.txt file and returns the  and prerelease tag from it.

**Namespace:** Nerdbank.GitVersioning

**Assembly:** NerdBank.GitVersioning (in NerdBank.GitVersioning.dll)
## Syntax
~~~~csharp
public static Nerdbank.GitVersioning.VersionOptions GetVersion(Commit commit, string repoRelativeProjectDirectory = null);
~~~~
##### Parameters
*commit*

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Type: Commit

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;The commit to read the version file from.


*repoRelativeProjectDirectory*

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Type: String

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;The directory to consider when searching for the version.txt file.


##### Return Value
Type: VersionOptions

The version information read from the file.

