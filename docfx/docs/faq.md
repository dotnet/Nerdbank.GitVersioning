# Frequently asked questions

## What is 'git height'?

Git 'height' is the number of commits in the longest path from HEAD (the code you're building)
to some origin point, inclusive. In this case the origin is the commit that set the major.minor
version number to the values found in HEAD.

For example, if the version specified at HEAD is 3.4 and the longest path in git history from HEAD
to where the version file was changed to 3.4 includes 15 commits, then the git height is "15".
Another example is when HEAD points directly at the commit that changed the major.minor version,
which has a git height of 1. [Learn more about 1 being the minimum revision number][GitHeightMinimum].

## Why is the git height used for the PATCH version component for public releases?

The git commit ID does not represent an alphanumerically sortable identifier
in semver, and thus delivers a poor package update experience for NuGet package
consumers. Incrementing the PATCH with each public release ensures that users
who want to update to your latest NuGet package will reliably get the latest
version.

The git height is guaranteed to always increase with each release within a given major.minor version,
assuming that each release builds on a previous release. And the height automatically resets when
the major or minor version numbers are incremented, which is also typically what you want.

## Why isn't the git commit ID included for public releases?

It could be, but the git height serves as a pseudo-identifier already and the
git commit id would just make it harder for users to type in the version
number if they ever had to.

Note that the git commit ID is *always* included in the
`AssemblyInformationalVersionAttribute` so one can always match a binary to the
exact version of source code that produced it.

Learn more about [public releases and the git commit ID suffix](public-vs-stable.md).

## How do I translate from a version to a git commit and vice versa?

While Nerdbank.GitVersioning calculates the version and applies it to most builds automatically,
there can be occasions where you want to do so yourself or reverse the process to determine
the commit that produced a given version.

To do this use [the `nbgv` tool](nbgv-cli.md) with the `get-version` or `get-commits` command.

Another (deprecated) option is to use a pair of Powershell scripts are included in the Nerdbank.GitVersioning NuGet package
that can help you to translate between the two representations.

    tools\Get-CommitId.ps1
    tools\Get-Version.ps1

`Get-CommitId.ps1` takes a version and print out the matching commit (or possible commits, in the exceptionally rare event of a collision).
`Get-Version.ps1` prints out the version information for the git commit current at HEAD.

## How do I build Nerdbank.GitVersioning from source?

Prerequisites and build instructions are found in our
[contributing guidelines](https://github.com/dotnet/Nerdbank.GitVersioning/tree/main/CONTRIBUTING.md).

## How do I consume the latest changes prior to their release on nuget.org?

We have [a public feed][PublicCI] where our CI pushes packages.
Adding the feed source URL to your nuget.config file will allow you to consume package versions that haven't been publicly released to nuget.org yet.

[PublicCI]: https://dev.azure.com/andrewarnott/OSS/_packaging?_a=feed&feed=PublicCI

## How do I temporarily disable Nerdbank.GitVersioning so I can build with a shallow clone?

Set the `NBGV_GitEngine` environment variable to `Disabled`.
