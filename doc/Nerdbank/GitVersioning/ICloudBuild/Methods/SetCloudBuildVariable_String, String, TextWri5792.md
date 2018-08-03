# ICloudBuild.SetCloudBuildVariable(String, String, TextWriter, TextWriter) Method
> Sets a cloud build variable, if supported.

**Namespace:** Nerdbank.GitVersioning

**Assembly:** NerdBank.GitVersioning (in NerdBank.GitVersioning.dll)
## Syntax
~~~~csharp
System.Collections.Generic.IReadOnlyDictionary<string, string> SetCloudBuildVariable(string name, string value, System.IO.TextWriter stdout, System.IO.TextWriter stderr);
~~~~
##### Parameters
*name*

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Type: String

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;The name of the variable.


*value*

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Type: String

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;The value for the variable.


*stdout*

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Type: TextWriter

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;An optional redirection for what should be written to the standard out stream.


*stderr*

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Type: TextWriter

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;An optional redirection for what should be written to the standard error stream.


##### Return Value
Type: IReadOnlyDictionary`2

A dictionary of environment/build variables that the caller should set to update the environment to match the new settings.

