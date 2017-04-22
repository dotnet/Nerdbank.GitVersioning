# Nerdbank.GitVersioning installation via NuGet 

Install the Nerdbank.GitVersioning package using the Visual Studio
NuGet Package Manager GUI, or the NuGet Package Manager Console: 

```
Install-Package Nerdbank-GitVersioning
```

If in a project that uses PackageReference for this package reference, you should manually add
`PrivateAssets="true"` to the PackageReference xml element to workaround
[this issue](https://github.com/AArnott/Nerdbank.GitVersioning/issues/122).

After installing this NuGet package, you may need to configure the version generation logic
in order for it to work properly.

When using packagse.config, the configuration is handled automatically via the tools\Install.ps1 script.
When using project.json or PackageReference, you can run the script tools\Create-VersionFile.ps1 to help
you create the version.json file and remove the old assembly attributes.

The scripts will look for the presence of a version.json or version.txt file.
If one already exists, nothing happens. If the version file does not exist,
the script looks in your project for the Properties\AssemblyInfo.cs file and attempts
to read the Major.Minor version number from
the AssemblyVersion attribute. It then generates a version.json file using the Major.Minor
that was parsed so that your assembly will build with the same AssemblyVersion as before,
which preserves backwards compatibility. Finally, it will remove the various version-related
assembly attributes from AssemblyInfo.cs.

If you did not use the scripts to configure the package, you may find that you get a
compilation failure because of multiple definitions of certain attributes such as
`AssemblyVersionAttribute`.
You should resolve these compilation errors by removing these attributes from your own
source code, as commonly found in your `Properties\AssemblyInfo.cs` file:

```csharp
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0-dev")]
```

If you are using the new VS 2017 .NET Core/Standard projects (or otherwise using the .NET SDK project type)
you won't see these attributes in your AssemblyInfo.cs file but you may still get compiler errors
due to duplicate attributes. In that case, a Rebuild of your project should resolve
[the issue](https://github.com/AArnott/Nerdbank.GitVersioning/issues/121).

This NuGet package creates these attributes at build time based on version information
found in your `version.json` file and your git repo's HEAD position.

When the package is installed, a version.json file is created in your project directory
(for packages.config clients). This ensures backwards compatibility where the installation of
this package will not cause the assembly version of the project to change. If you would
like the same version number to be applied to all projects in the repo, then you may move
the version.json file to the root directory of your git repo.

Note: After first installing the package, you need to commit the version file so that
it will be picked up during the build's version generation. If you build prior to committing,
the version number produced will be 0.0.x.

# Next steps 

You must also create [a version.json file](versionJson.md) in your repo. 
Learn more about [how .NET projects are stamped with version information](dotnet.md).
