# VersionOptions.Inherit Property
> Gets or sets a value indicating whether this options object should inherit from an ancestor any settings that are not explicitly set in this one.

**Namespace:** Nerdbank.GitVersioning

**Assembly:** NerdBank.GitVersioning (in NerdBank.GitVersioning.dll)
## Syntax
~~~~csharp
[JsonProperty(/*Could not decode attribute arguments.*/)]
public bool Inherit
{
	[System.Runtime.CompilerServices.CompilerGenerated]
	get;
	[System.Runtime.CompilerServices.CompilerGenerated]
	set;
}
~~~~
## Remarks
When this is true, this object may not completely describe the options to be applied.