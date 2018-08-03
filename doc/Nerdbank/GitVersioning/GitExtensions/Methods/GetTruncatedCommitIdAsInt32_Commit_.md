# GitExtensions.GetTruncatedCommitIdAsInt32(Commit) Method
> Takes the first 4 bytes of a commit ID (i.e. first 8 characters of its hex-encoded SHA) and returns them as an integer.

**Namespace:** Nerdbank.GitVersioning

**Assembly:** NerdBank.GitVersioning (in NerdBank.GitVersioning.dll)
## Syntax
~~~~csharp
public static int GetTruncatedCommitIdAsInt32(this Commit commit);
~~~~
##### Parameters
*commit*

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Type: Commit

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;The commit to identify with an integer.


##### Return Value
Type: Int32

The integer which identifies a commit.

