# MSBuild

Installing the Nerdbank.GitVersioning package from NuGet into your
MSBuild-based projects 

## Public releases

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

