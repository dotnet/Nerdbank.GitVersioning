# ICloudBuild Interface
> Defines cloud build provider functionality.

**Namespace:** Nerdbank.GitVersioning

**Assembly:** NerdBank.GitVersioning (in NerdBank.GitVersioning.dll)
## Inheritance Hierarchy
&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Nerdbank.GitVersioning.ICloudBuild

## Syntax
~~~~csharp
public interface ICloudBuild
~~~~
## Properties
|Name|Description|
|---|---|
|[IsApplicable](/doc/Nerdbank/GitVersioning/ICloudBuild/Properties/IsApplicable.md)|Gets a value indicating whether the active cloud build matches what this instance supports.|
|[IsPullRequest](/doc/Nerdbank/GitVersioning/ICloudBuild/Properties/IsPullRequest.md)|Gets a value indicating whether a cloud build is validating a pull request.|
|[BuildingBranch](/doc/Nerdbank/GitVersioning/ICloudBuild/Properties/BuildingBranch.md)|Gets the branch being built by a cloud build, if applicable.|
|[BuildingTag](/doc/Nerdbank/GitVersioning/ICloudBuild/Properties/BuildingTag.md)|Gets the tag being built by a cloud build, if applicable.|
|[GitCommitId](/doc/Nerdbank/GitVersioning/ICloudBuild/Properties/GitCommitId.md)|Gets the git commit ID being built by a cloud build, if applicable.|
## Methods
|Name|Description|
|---|---|
|[SetCloudBuildNumber(String, TextWriter, TextWriter)](/doc/Nerdbank/GitVersioning/ICloudBuild/Methods/SetCloudBuildNumber_String%2c%20TextWriter%2c%20TextW4896.md)|Sets the build number for the cloud build, if supported.|
|[SetCloudBuildVariable(String, String, TextWriter, TextWriter)](/doc/Nerdbank/GitVersioning/ICloudBuild/Methods/SetCloudBuildVariable_String%2c%20String%2c%20TextWri5792.md)|Sets a cloud build variable, if supported.|
