# VersionFile.IsVersionDefined(String) Method
> Checks whether the version.txt file is defined in the specified project directory
            or one of its ancestors.

**Namespace:** Nerdbank.GitVersioning

**Assembly:** NerdBank.GitVersioning (in NerdBank.GitVersioning.dll)
## Syntax
~~~~csharp
public static bool IsVersionDefined(string projectDirectory);
~~~~
##### Parameters
*projectDirectory*

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Type: String

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;The directory to start searching within.


##### Return Value
Type: Boolean

true if the version.txt file is found; otherwise false.

