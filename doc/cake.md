# Cake Build
Add `#addin Cake.GitVersioning` to the top of your Cake Build script.  See [here](doc/Cake/GitVersioning/GitVersioningAliases) for usage.  See [here](doc/Nerdbank/GitVersioning/VersionOracle) for the VersionOracle usage.

## Example
~~~~csharp
Task("GetVersion")
    .Does(() =>
{
    Information(GetVersioningGetVersion().SemVer2)
});
~~~~