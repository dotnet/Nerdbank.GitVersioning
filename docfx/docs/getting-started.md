# Getting Started

## Installation and Configuration

You can install Nerdbank.GitVersioning into your projects via NuGet or NPM.

* Use the [nbgv .NET Core CLI tool](nbgv-cli.md) (recommended)
* [NuGet installation instructions](nuget-acquisition.md)
* [NPM installation instructions](npm-acquisition.md)
* [Cake Build installation instructions](build-systems/cake.md)

You must also create [a `version.json` file](versionJson.md) in your repo. See [migration notes](migrating.md) if your repo already has a `version.txt` or `version.json` file from using another system.

## How to leverage version stamping and runtime information

See relevant documentation for any of these topics:

* [.NET](ecosystems/dotnet.md)
* [Node](ecosystems/node.md)
* [VSIX](ecosystems/vsix.md)

## Build

We have docs to describe how to build with Nerdbank.GitVersioning
for these build systems:

* [MSBuild](build-systems/msbuild.md)
* [gulp](build-systems/gulp.md)
* [Cake Build](build-systems/cake.md)

Also some special [cloud build considerations](cloudbuild.md) (e.g. Azure Pipelines, GitHub Actions, etc.).
