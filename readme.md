# Nerdbank.GitVersioning

[![Join the chat at https://gitter.im/AArnott/Nerdbank.GitVersioning](https://badges.gitter.im/Join%20Chat.svg)](https://gitter.im/AArnott/Nerdbank.GitVersioning?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge)

## Overview

This package adds precise, semver-compatible git commit information
to every assembly, VSIX, and NuGet package.
It implicitly supports all cloud build services and CI server software
because it simply uses git itself and integrates naturally in MSBuild. 

What sets this package apart from other git-based versioning projects is:

1. Prioritize absolute build reproducibility. Every single commit can be built and produce a unique version.
2. No dependency on tags. Tags can be added to existing commits at any time. Clones may not fetch tags. No dependency on tags means better build reproducibility.
3. No dependency on branch names. Branches come and go, and a commit may belong to any number of branches. Regardless of the branch HEAD may be attached to, the build should be identical.
4. The computed version information is based on an author-defined major.minor version and an optional unstable tag, plus a shortened git commit ID.

## Installation

After installing this NuGet package, you may find that you get a compilation failure
because of multiple definitions of certain attributes such as `AssemblyVersionAttribute`.
You should resolve these compilation errors by removing these attributes from your own
source code, as commonly found in your `Properties\AssemblyInfo.cs` file:

```csharp
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0-dev")]
```

This NuGet package creates these attributes at build time based on version information
found in your `version.txt` file and your git repo's HEAD position.

### version.txt file

You must define a version.txt file in your project directory or some ancestor of it.
By convention it is often found in the root directory of your git repo.

Here is the content of a sample version.txt file you may start with (do not indent):

    1.0.0
    -beta

### version.txt file format

The format of the version.txt file is as follows:
LINE 1: x.y.z
LINE 2: -prerelease

The `x` and `y` variables are for your use to specify a version that is meaningful
to your customers. Consider using [semantic versioning][semver] for guidance.
The `z` variable should be 0.

The second line is optional and allows you to indicate that you are building
prerelease software. 

### Apply to VSIX versions

Besides simply installing the NuGet package into your VSIX-generating project,
you should also open the `source.extension.vsixmanifest` file in a code editor
and set the `PackageManifest/Metadata/Identity/@Version` attribute to this
value: `|YourProjectName;GetBuildVersion|` where `YourProjectName` is
obviously replaced with the actual project name (without extension) of your
VSIX project.

### Apply to NuProj built NuPkg versions

You will need to manually make the following changes to your NuProj file
in order for it to build NuPkg files based on versions computed by this package:

1. Remove any definition of a Version property:

    ```xml
    <Version>1.0.0-beta1</Version>
    ```

2. Add this property definition:

    ```xml
    <VersionDependsOn>$(VersionDependsOn);GetNuPkgVersion</VersionDependsOn>
    ```

3. Add these targets and imports (changing the version number in the paths as necessary):

    ```xml
    <Target Name="EnsureNuGetPackageBuildImports" BeforeTargets="PrepareForBuild">
        <PropertyGroup>
            <ErrorText>This project references NuGet package(s) that are missing on this computer. Use NuGet Package Restore to download them.  For more information, see http://go.microsoft.com/fwlink/?LinkID=322105. The missing file is {0}.</ErrorText>
        </PropertyGroup>
        <Error Condition="!Exists('..\packages\Nerdbank.GitVersioning.1.1.2-rc\build\NerdBank.GitVersioning.targets')" Text="$([System.String]::Format('$(ErrorText)', '..\packages\Nerdbank.GitVersioning.1.1.2-rc\build\NerdBank.GitVersioning.targets'))" />
    </Target>
    <Import Project="..\packages\Nerdbank.GitVersioning.1.1.2-rc\build\NerdBank.GitVersioning.targets" Condition="Exists('..\packages\Nerdbank.GitVersioning.1.1.2-rc\build\NerdBank.GitVersioning.targets')" />
    <Target Name="GetNuPkgVersion" DependsOnTargets="GetBuildVersion">
        <PropertyGroup>
            <Version>$(NuGetPackageVersion)</Version>
        </PropertyGroup>
    </Target>
    ```

## Build

By default, each build of a Nuget package will include the git commit ID.
When you are preparing a release (whether a stable or unstable prerelease),
you may build setting the `PublicRelease` global property to `true`
in order to avoid the git commit ID being included in the NuGet package version.

From the command line, building a release version might look like this:

    msbuild /p:PublicRelease=true

Note you may consider passing this switch to any build that occurs in the
branch that you publish released NuGet packages from. 
You should only build with this property set from one release branch per
major.minor version to avoid the risk of producing multiple unique NuGet
packages with a colliding version spec.

## Where and how versions are calculated and applied

This package calculates the version based on a combination of the version.txt file,
the git 'height' of the version, and the git commit ID.

### Assembly version generation

During the build it adds source code such as this to your compilation:

```csharp
[assembly: System.Reflection.AssemblyVersion("1.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.24.15136")]
[assembly: System.Reflection.AssemblyInformationalVersion("1.0.24.15136-alpha+g9a7eb6c819")]
```

The first and second integer components of the versions above come from the 
version.txt file.
The third integer component of the version here is the height of your git history up to
that point, such that it reliably increases with each release.
The -alpha tag also comes from the version.txt file and indicates this is an
unstable version.
The -g9a7eb6c819 tag is the concatenation of -g and the git commit ID that was built.

This class is also injected into your project at build time:

```csharp
internal sealed partial class ThisAssembly {
    internal const string AssemblyVersion = "1.0";
    internal const string AssemblyFileVersion = "1.0.24.15136";
    internal const string AssemblyInformationalVersion = "1.0.24.15136-alpha+g9a7eb6c819";
}
```

This allows you to actually write source code that can refer to the exact build
number your assembly will be assigned.

### NuGet package version generation

Given the same settings as used in the discussion above, a NuGet package may be
assigned this version: 

    1.0.24-alpha-g9a7eb6c819

When built with the `/p:PublicRelease=true` switch, the NuGet version becomes:

    1.0.24-alpha

## Frequently asked questions

### Why is the git height used for the PATCH version component for public releases?

The git commit ID does not represent an alphanumerically sortable identifier
in semver, and thus delivers a poor package update experience for NuGet package
consumers. Incrementing the PATCH with each public release ensures that users
who want to update to your latest NuGet package will reliably get the latest
version. 

The git height is guaranteed to always increase with each release, assuming
that each release builds on a previous release.

### Why isn't the git commit ID included for public releases?

It could be, but the git height serves as a pseudo-identifier already and the
git commit id would just make it harder for users to type in the version
number if they ever had to.

Note that the git commit ID is *always* included in the 
`AssemblyInformationVersionAttribute` so one can always match a binary to the
exact version of source code that produced it.

 [semver]: http://semver.org
