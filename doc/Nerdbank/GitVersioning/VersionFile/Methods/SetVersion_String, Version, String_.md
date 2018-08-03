# VersionFile.SetVersion(String, Version, String) Method
> Writes the version.txt file to a directory within a repo with the specified version information.

**Namespace:** Nerdbank.GitVersioning

**Assembly:** NerdBank.GitVersioning (in NerdBank.GitVersioning.dll)
## Syntax
~~~~csharp
public static string SetVersion(string projectDirectory, System.Version version, string unstableTag = null);
~~~~
##### Parameters
*projectDirectory*

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Type: String

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;
            The path to the directory in which to write the version.txt file.
            The file's impact will be all descendent projects and directories from this specified directory,
            except where any of those directories have their own version.txt file.
            


*version*

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Type: Version

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;The version information to write to the file.


*unstableTag*

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Type: String

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;The optional unstable tag to include in the file.


##### Return Value
Type: String

The path to the file written.

