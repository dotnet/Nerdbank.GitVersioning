# GitExtensions Class
> Git extension methods.

**Namespace:** Nerdbank.GitVersioning

**Assembly:** NerdBank.GitVersioning (in NerdBank.GitVersioning.dll)
## Inheritance Hierarchy
&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;System.Object

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Nerdbank.GitVersioning.GitExtensions

## Syntax
~~~~csharp
public static class GitExtensions
~~~~
## Methods
|Name|Description|
|---|---|
|[GetVersionHeight(Commit, String, Version)](/doc/Nerdbank/GitVersioning/GitExtensions/Methods/GetVersionHeight_Commit%2c%20String%2c%20Version_.md)|Gets the number of commits in the longest single path between
            the specified commit and the most distant ancestor (inclusive)
            that set the version to the value at .|
|[GetVersionHeight(Repository, String)](/doc/Nerdbank/GitVersioning/GitExtensions/Methods/GetVersionHeight_Repository%2c%20String_.md)|Gets the number of commits in the longest single path between
            HEAD in a repo and the most distant ancestor (inclusive)
            that set the version to the value in the working copy
            (or HEAD for bare repositories).|
|[GetVersionHeight(Branch, String)](/doc/Nerdbank/GitVersioning/GitExtensions/Methods/GetVersionHeight_Branch%2c%20String_.md)|Gets the number of commits in the longest single path between
            the specified commit and the most distant ancestor (inclusive)
            that set the version to the value at the tip of the .|
|[GetTruncatedCommitIdAsInt32(Commit)](/doc/Nerdbank/GitVersioning/GitExtensions/Methods/GetTruncatedCommitIdAsInt32_Commit_.md)|Takes the first 4 bytes of a commit ID (i.e. first 8 characters of its hex-encoded SHA)
            and returns them as an integer.|
|[GetTruncatedCommitIdAsUInt16(Commit)](/doc/Nerdbank/GitVersioning/GitExtensions/Methods/GetTruncatedCommitIdAsUInt16_Commit_.md)|Takes the first 2 bytes of a commit ID (i.e. first 4 characters of its hex-encoded SHA)
            and returns them as an 16-bit unsigned integer.|
|[GetCommitFromTruncatedIdInteger(Repository, Int32)](/doc/Nerdbank/GitVersioning/GitExtensions/Methods/GetCommitFromTruncatedIdInteger_Repository%2c%20Int32_.md)|Looks up a commit by an integer that captures the first for bytes of its ID.|
|[GetCommitFromVersion(Repository, Version, String)](/doc/Nerdbank/GitVersioning/GitExtensions/Methods/GetCommitFromVersion_Repository%2c%20Version%2c%20String_.md)|Looks up the commit that matches a specified version number.|
|[GetCommitsFromVersion(Repository, Version, String)](/doc/Nerdbank/GitVersioning/GitExtensions/Methods/GetCommitsFromVersion_Repository%2c%20Version%2c%20String_.md)|Looks up the commits that match a specified version number.|
|[FindLibGit2NativeBinaries(String)](/doc/Nerdbank/GitVersioning/GitExtensions/Methods/FindLibGit2NativeBinaries_String_.md)|Finds the directory that contains the appropriate native libgit2 module.|
|[OpenGitRepo(String)](/doc/Nerdbank/GitVersioning/GitExtensions/Methods/OpenGitRepo_String_.md)|Opens a  found at or above a specified path.|
