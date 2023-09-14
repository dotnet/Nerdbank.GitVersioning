This is a [Cake Build](https://cakebuild.net/) plugin that adds [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning#readme) functionality to your build.

## Usage

Add `#addin Cake.GitVersioning` to the top of your Cake Build script.  See [here](https://github.com/dotnet/Nerdbank.GitVersioning/wiki/GitVersioningAliases) for usage.  See [here](https://github.com/dotnet/Nerdbank.GitVersioning/wiki/VersionOracle) for the VersionOracle usage.

### Example
~~~~csharp
Task("GetVersion")
    .Does(() =>
{
    Information(GitVersioningGetVersion().SemVer2)
});
~~~~
