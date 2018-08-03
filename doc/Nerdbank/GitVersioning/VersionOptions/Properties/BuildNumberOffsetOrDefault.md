# VersionOptions.BuildNumberOffsetOrDefault Property
> Gets a number to add to the git height when calculating the  number.

**Namespace:** Nerdbank.GitVersioning

**Assembly:** NerdBank.GitVersioning (in NerdBank.GitVersioning.dll)
## Syntax
~~~~csharp
[JsonIgnore]
public int BuildNumberOffsetOrDefault
{
get;
}
~~~~
## Remarks
An error will result if this value is negative with such a magnitude as to exceed the git height,
            resulting in a negative build number.