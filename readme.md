# Nerdbank.GitVersioning

[![Build Status](https://dev.azure.com/andrewarnott/OSS/_apis/build/status/Nerdbank.GitVersioning)](https://dev.azure.com/andrewarnott/OSS/_build/latest?definitionId=18)
[![NuGet package](https://img.shields.io/nuget/v/Nerdbank.GitVersioning.svg)](https://nuget.org/packages/Nerdbank.GitVersioning)
[![NuGet downloads](https://img.shields.io/nuget/dt/Nerdbank.GitVersioning.svg)](https://nuget.org/packages/Nerdbank.GitVersioning)
[![NPM package](https://img.shields.io/npm/v/nerdbank-gitversioning.svg)](https://www.npmjs.com/package/nerdbank-gitversioning)
[![Join the chat at https://gitter.im/AArnott/Nerdbank.GitVersioning](https://badges.gitter.im/Join%20Chat.svg)](https://gitter.im/AArnott/Nerdbank.GitVersioning?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge)

## Overview

This package adds precise, semver-compatible git commit information
to every assembly, VSIX, NuGet and NPM package, and more.
It implicitly supports all cloud build services and CI server software
because it simply uses git itself and integrates naturally in MSBuild, gulp
and other build scripts.

What sets this package apart from other git-based versioning projects is:

1. Prioritize absolute build reproducibility. Every single commit can be built and produce a unique version.
2. No dependency on tags. Tags can be added to existing commits at any time. Clones may not fetch tags. No dependency on tags means better build reproducibility.
3. No dependency on branch names. Branches come and go, and a commit may belong to any number of branches. Regardless of the branch HEAD may be attached to, the build should be identical.
4. The computed version information is based on an author-defined major.minor version and an optional unstable tag, plus a shortened git commit ID.

## Installation and Configuration

You can install Nerdbank.GitVersioning into your projects via NuGet or NPM.

* Use the [nbgv .NET Core CLI tool](doc/nbgv-cli.md) (recommended)
* [NuGet installation instructions](doc/nuget-acquisition.md)
* [NPM installation instructions](doc/npm-acquisition.md)
* [Cake Build installation instructions](doc/cake.md)

You must also create [a version.json file](doc/versionJson.md) in your repo. See [migration notes](doc/migrating.md) if your repo already has a version.txt or version.json file from using another system.

## How to leverage version stamping and runtime information

See relevant documentation for any of these topics:

* [.NET](doc/dotnet.md)
* [Node](doc/node.md)
* [VSIX](doc/vsix.md)
* [NuProj](doc/nuproj.md)

## Build

We have docs to describe how to build with Nerdbank.GitVersioning
for these build systems:

* [MSBuild](doc/msbuild.md)
* [gulp](doc/gulp.md)
* [DNX](doc/dotnet-cli.md)
* [dotnet CLI](doc/dotnet-cli.md)
* [Cake Build](doc/cake.md)

Also some special [cloud build considerations](doc/cloudbuild.md).

## Where and how versions are calculated and applied

This package calculates the version based on a combination of the version.json file,
the git 'height' of the version, and the git commit ID.

### Version generation

Given the same settings as used in the discussion above, a NuGet or NPM package may be
assigned this version:

    1.0.24-alpha-g9a7eb6c819

When built as a public release, the git commit ID is dropped:

    1.0.24-alpha

## Frequently asked questions

### What is 'git height'?

Git 'height' is the number of commits in the longest path from HEAD (the code you're building)
to some origin point, inclusive. In this case the origin is the commit that set the major.minor
version number to the values found in HEAD.

For example, if the version specified at HEAD is 3.4 and the longest path in git history from HEAD
to where the version file was changed to 3.4 includes 15 commits, then the git height is "15".
Another example is when HEAD points directly at the commit that changed the major.minor version,
which has a git height of 1. [Learn more about 1 being the minimum revision number][GitHeightMinimum].

### Why is the git height used for the PATCH version component for public releases?

The git commit ID does not represent an alphanumerically sortable identifier
in semver, and thus delivers a poor package update experience for NuGet package
consumers. Incrementing the PATCH with each public release ensures that users
who want to update to your latest NuGet package will reliably get the latest
version.

The git height is guaranteed to always increase with each release within a given major.minor version,
assuming that each release builds on a previous release. And the height automatically resets when
the major or minor version numbers are incremented, which is also typically what you want.

### Why isn't the git commit ID included for public releases?

It could be, but the git height serves as a pseudo-identifier already and the
git commit id would just make it harder for users to type in the version
number if they ever had to.

Note that the git commit ID is *always* included in the
`AssemblyInformationVersionAttribute` so one can always match a binary to the
exact version of source code that produced it.

### How do I translate from a version to a git commit and vice versa?

A pair of Powershell scripts are included in the Nerdbank.GitVersioning NuGet package
that can help you to translate between the two representations.

    tools\Get-CommitId.ps1
    tools\Get-Version.ps1

`Get-CommitId.ps1` takes a version and print out the matching commit (or possible commits, in the exceptionally rare event of a collision).
`Get-Version.ps1` prints out the version information for the git commit current at HEAD.

### How do I build Nerdbank.GitVersioning from source?

Prerequisites and build instructions are found in our
[contributing guidelines](CONTRIBUTING.md).

 [semver]: http://semver.org
 [GitHeightMinimum]: https://github.com/AArnott/Nerdbank.GitVersioning/issues/102#issuecomment-269591960
