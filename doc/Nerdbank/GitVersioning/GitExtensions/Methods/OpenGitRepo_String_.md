# GitExtensions.OpenGitRepo(String) Method
> Opens a  found at or above a specified path.

**Namespace:** Nerdbank.GitVersioning

**Assembly:** NerdBank.GitVersioning (in NerdBank.GitVersioning.dll)
## Syntax
~~~~csharp
public static Repository OpenGitRepo(string pathUnderGitRepo);
~~~~
##### Parameters
*pathUnderGitRepo*

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Type: String

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;The path at or beneath the git repo root.


##### Return Value
Type: Repository

The  found for the specified path, or null if no git repo is found.

