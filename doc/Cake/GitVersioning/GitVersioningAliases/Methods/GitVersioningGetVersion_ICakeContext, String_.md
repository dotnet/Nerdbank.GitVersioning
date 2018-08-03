# GitVersioningAliases.GitVersioningGetVersion(ICakeContext, String) Method
> Gets the Git Versioning version from the current repo.

**Namespace:** Cake.GitVersioning

**Assembly:** Cake.GitVersioning (in Cake.GitVersioning.dll)
## Syntax
~~~~csharp
[Cake.Core.Annotations.CakeMethodAlias]
public static Nerdbank.GitVersioning.VersionOracle GitVersioningGetVersion(this Cake.Core.ICakeContext context, string projectDirectory = ".");
~~~~
##### Parameters
*context*

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Type: ICakeContext

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;The context.


*projectDirectory*

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Type: String

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Directory to start the search for version.json.


##### Return Value
Type: VersionOracle

The version information from Git Versioning.

