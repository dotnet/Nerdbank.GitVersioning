# VersionOptions.BuildNumberOffset Property
> Gets or sets a number to add to the git height when calculating the  number.

**Namespace:** Nerdbank.GitVersioning

**Assembly:** NerdBank.GitVersioning (in NerdBank.GitVersioning.dll)
## Syntax
~~~~csharp
[JsonProperty(/*Could not decode attribute arguments.*/)]
public int? BuildNumberOffset
{
[System.Runtime.CompilerServices.CompilerGenerated]
get;
[System.Runtime.CompilerServices.CompilerGenerated]
set;
}
~~~~
## Remarks
An error will result if this value is negative with such a magnitude as to exceed the git height,
            resulting in a negative build number.