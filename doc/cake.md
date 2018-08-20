# Cake Build
Add `#addin Cake.GitVersioning` to the top of your Cake Build script.  See [here](https://github.com/AArnott/Nerdbank.GitVersioning/wiki/GitVersioningAliases) for usage.  See [here](https://github.com/AArnott/Nerdbank.GitVersioning/wiki/VersionOracle) for the VersionOracle usage.

## Example
~~~~csharp
Task("GetVersion")
    .Does(() =>
{
    Information(GetVersioningGetVersion().SemVer2)
});
~~~~
