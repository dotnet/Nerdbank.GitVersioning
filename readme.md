# Nerdbank.GitVersioning

## Installation

After installing this NuGet package, you may find that you get a compilation failure
because of multiple definitions of certain attributes such as `AssemblyVersionAttribute`.
You should resolve these compilation errors by removing these attributes from your own
source code, as commonly found in your `Properties\AssemblyInfo.cs` file:

    [assembly: AssemblyVersion("1.0.0.0")]
    [assembly: AssemblyFileVersion("1.0.0.0")]
    [assembly: AssemblyInformationalVersion("1.0.0-dev")]

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

### Apply to NuProj built NuPkg versions

You will need to manually make the following changes to your NuProj file
in order for it to build NuPkg files based on versions computed by this package:

1. Remove any definition of a Version property:

        <Version>1.0.0-beta1</Version>

2. Add this property definition:


        <VersionDependsOn>$(VersionDependsOn);GetNuPkgVersion</VersionDependsOn>

3. Add these targets and imports (changing the version number in the paths as necessary):


        <Target Name="EnsureNuGetPackageBuildImports" BeforeTargets="PrepareForBuild">
          <PropertyGroup>
            <ErrorText>This project references NuGet package(s) that are missing on this computer. Use NuGet Package Restore to download them.  For more information, see http://go.microsoft.com/fwlink/?LinkID=322105. The missing file is {0}.</ErrorText>
          </PropertyGroup>
          <Error Condition="!Exists('..\packages\Nerdbank.GitVersioning.1.0.15136-beta\build\NerdBank.GitVersioning.targets')" Text="$([System.String]::Format('$(ErrorText)', '..\packages\Nerdbank.GitVersioning.1.0.15136-beta\build\NerdBank.GitVersioning.targets'))" />
        </Target>
        <Import Project="..\packages\Nerdbank.GitVersioning.1.0.15136-beta\build\NerdBank.GitVersioning.targets" Condition="Exists('..\packages\Nerdbank.GitVersioning.1.0.15136-beta\build\NerdBank.GitVersioning.targets')" />
        <Target Name="GetNuPkgVersion" DependsOnTargets="GetBuildVersion">
          <PropertyGroup>
            <Version>$(NuGetPackageVersion)</Version>
          </PropertyGroup>
        </Target>

## Build

By default, each build will fix the PATCH component of the version number to 0.
When you are preparing a release (whether a stable or unstable prerelease),
you may build setting the `UseNonZeroBuildNumber` global property to `true`
in order to switch from appending the git commit ID to the semver-compliant
version and instead the PATCH component will be set to a non-zero value that
increments with the calendar date.

From the command line, building a release version might look like this:

    msbuild /p:UseNonZeroBuildNumber=true

Note you may consider passing this switch to any build that occurs at a
frequency of at most once per day. Building with this switch more than once
per day may generate NuGet packages that have the same version but different
content, which is discouraged.

## Where and how versions are calculated and applied

This package calculates the version based on a combination of the version.txt file
the calendar date, and the git commit ID. 

### Assembly version generation

During the build it adds source code such as this to your compilation:

    [assembly: System.Reflection.AssemblyVersion("1.0")]
    [assembly: System.Reflection.AssemblyFileVersion("1.0.15136")]
    [assembly: System.Reflection.AssemblyInformationalVersion("1.0.15136-alpha+g9a7eb6c819")]

The first and second integer components of the versions above come from the 
version.txt file.
The third integer component of the version here is the jdate, which is the last
two digits of the year, and then the number of the day of the year (disregarding
months).
The -alpha tag also comes from the version.txt file and indicates this is an
unstable version.
The -g9a7eb6c819 tag is the concatenation of -g and the git commit ID that was built.

This class is also injected into your project at build time:

    internal sealed partial class ThisAssembly {
        internal const string AssemblyVersion = "1.0";
        internal const string AssemblyFileVersion = "1.0.15136";
        internal const string AssemblyInformationalVersion = "1.0.15136-alpha+g9a7eb6c819";
    }

This allows you to actually write source code that can refer to the exact build
number your assembly will be assigned.

### NuGet package version generation

Given the same settings as used in the discussion above, a NuGet package may be
assigned this version: 

    1.0.0-alpha-g9a7eb6c819

When built with the `/p:UseNonZeroBuildNumber=true` switch, the NuGet version becomes:

    1.0.15136-alpha

## Frequently asked questions

### Why is the PATCH version component fixed to 0 for non-release builds?

This is for reproducibility in between releases. When under active development,
a project may build any number of times per day on any number of machines.
Other projects that are also under development may need to take a dependency on
one of these builds that occur in between public releases. It is important that
a build that occurs on a dev box be exactly reproducible by others and by an
official build machine or cloud build. The jdate that would otherwise be used
for the PATCH component of the version represents a non-repeatable component
since a build the next day would produce a different result. So we fix it to
zero instead.

### Why is the jdate used for the PATCH version component for public releases?

The git commit ID does not represent an alphanumerically sortable identifier
in semver, and thus delivers a poor package update experience for NuGet package
consumers. Incrementing the PATCH with each public release ensures that users
who want to update to your latest NuGet package will reliably get the latest
version. 

### Why isn't the git commit ID included for public releases?

It could be, but the jdate serves as a unique identifier already and the
git commit id would just make it harder for users to type in the version
number if they ever had to.

Note that the git commit ID is *always* included in the 
`AssemblyInformationVersionAttribute` so one can always match a binary to the
exact version of source code that produced it.

 [semver]: http://semver.org
