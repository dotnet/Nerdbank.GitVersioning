This is a [Cake Build](https://cakebuild.net/) plugin that adds [Nerdbank.GitVersioning](https://dotnet.github.io/Nerdbank.GitVersioning/) functionality to your build.

## Usage

Add `#addin Cake.GitVersioning` to the top of your Cake Build script.  See [here](https://github.com/dotnet/Nerdbank.GitVersioning/wiki/GitVersioningAliases) for usage.  See [here](https://github.com/dotnet/Nerdbank.GitVersioning/wiki/VersionOracle) for the VersionOracle usage.

### Example

```cs
Task("GetVersion")
    .Does(() =>
{
    Information(GitVersioningGetVersion().SemVer2)
});
```
