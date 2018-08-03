# GitExtensions.GetVersionHeight(Branch, String) Method
> Gets the number of commits in the longest single path between the specified commit and the most distant ancestor (inclusive) that set the version to the value at the tip of the .

**Namespace:** Nerdbank.GitVersioning

**Assembly:** NerdBank.GitVersioning (in NerdBank.GitVersioning.dll)
## Syntax
~~~~csharp
public static int GetVersionHeight(this Branch branch, string repoRelativeProjectDirectory = null);
~~~~
##### Parameters
*branch*

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Type: Branch

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;The branch to measure the height of.


*repoRelativeProjectDirectory*

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Type: String

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;The repo-relative project directory for which to calculate the version.


##### Return Value
Type: Int32

The height of the branch till the version is changed.

