# Cake Build
Add `#addin Cake.GitVersioning` to the top of your Cake Build script.  See [here](Cake/GitVersioning/GitVersioningAliases.md) for usage.  See [here](Nerdbank/GitVersioning/VersionOracle.md) for the VersionOracle usage.

## Example
~~~~csharp
Task("GetVersion")
    .Does(() =>
{
    Information(GetVersioningGetVersion().SemVer2)
});
~~~~