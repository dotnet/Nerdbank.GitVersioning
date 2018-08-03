# GitExtensions.GetVersionHeight(Repository, String) Method
> Gets the number of commits in the longest single path between HEAD in a repo and the most distant ancestor (inclusive) that set the version to the value in the working copy (or HEAD for bare repositories).

**Namespace:** Nerdbank.GitVersioning

**Assembly:** NerdBank.GitVersioning (in NerdBank.GitVersioning.dll)
## Syntax
~~~~csharp
public static int GetVersionHeight(this Repository repo, string repoRelativeProjectDirectory = null);
~~~~
##### Parameters
*repo*

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Type: Repository

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;The repo with the working copy / HEAD to measure the height of.


*repoRelativeProjectDirectory*

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Type: String

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;The repo-relative project directory for which to calculate the version.


##### Return Value
Type: Int32

The height of the repo at HEAD. Always a positive integer.

