# VersionFile.GetVersion(Repository, String) Method
> Reads the version.txt file and returns the  and prerelease tag from it.

**Namespace:** Nerdbank.GitVersioning

**Assembly:** NerdBank.GitVersioning (in NerdBank.GitVersioning.dll)
## Syntax
~~~~csharp
public static Nerdbank.GitVersioning.VersionOptions GetVersion(Repository repo, string repoRelativeProjectDirectory = null);
~~~~
##### Parameters
*repo*

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Type: Repository

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;The repo to read the version file from.


*repoRelativeProjectDirectory*

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Type: String

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;The directory to consider when searching for the version.txt file.


##### Return Value
Type: VersionOptions

The version information read from the file.

