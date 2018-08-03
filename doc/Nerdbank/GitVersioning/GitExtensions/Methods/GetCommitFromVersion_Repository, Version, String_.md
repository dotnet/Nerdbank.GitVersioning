# GitExtensions.GetCommitFromVersion(Repository, Version, String) Method
> Looks up the commit that matches a specified version number.

**Namespace:** Nerdbank.GitVersioning

**Assembly:** NerdBank.GitVersioning (in NerdBank.GitVersioning.dll)
## Syntax
~~~~csharp
public static Commit GetCommitFromVersion(this Repository repo, System.Version version, string repoRelativeProjectDirectory = null);
~~~~
##### Parameters
*repo*

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Type: Repository

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;The repository to search for a matching commit.


*version*

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Type: Version

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;The version previously obtained from .


*repoRelativeProjectDirectory*

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Type: String

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;
            The repo-relative project directory from which  was originally calculated.
            


##### Return Value
Type: Commit

The matching commit, or null if no match is found.

