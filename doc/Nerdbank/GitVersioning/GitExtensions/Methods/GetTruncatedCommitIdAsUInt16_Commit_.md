# GitExtensions.GetTruncatedCommitIdAsUInt16(Commit) Method
> Takes the first 2 bytes of a commit ID (i.e. first 4 characters of its hex-encoded SHA)
            and returns them as an 16-bit unsigned integer.

**Namespace:** Nerdbank.GitVersioning

**Assembly:** NerdBank.GitVersioning (in NerdBank.GitVersioning.dll)
## Syntax
~~~~csharp
public static ushort GetTruncatedCommitIdAsUInt16(this Commit commit);
~~~~
##### Parameters
*commit*

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Type: Commit

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;The commit to identify with an integer.


##### Return Value
Type: UInt16

The unsigned integer which identifies a commit.

