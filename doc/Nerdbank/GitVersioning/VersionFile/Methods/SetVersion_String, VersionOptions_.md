# VersionFile.SetVersion(String, VersionOptions) Method
> Writes the version.json file to a directory within a repo with the specified version information.

**Namespace:** Nerdbank.GitVersioning

**Assembly:** NerdBank.GitVersioning (in NerdBank.GitVersioning.dll)
## Syntax
~~~~csharp
public static string SetVersion(string projectDirectory, Nerdbank.GitVersioning.VersionOptions version);
~~~~
##### Parameters
*projectDirectory*

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Type: String

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;
            The path to the directory in which to write the version.json file.
            The file's impact will be all descendent projects and directories from this specified directory,
            except where any of those directories have their own version.json file.
            


*version*

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Type: VersionOptions

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;The version information to write to the file.


##### Return Value
Type: String

The path to the file written.

