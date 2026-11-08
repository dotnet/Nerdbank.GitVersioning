# Getting Started

## Installation and Configuration

You can install Nerdbank.GitVersioning into your projects via NuGet or NPM. Source-preview
adapters are also available for Rust, Go, Gradle, and Maven.

* Use the [nbgv .NET Core CLI tool](nbgv-cli.md) (recommended)
* [NuGet installation instructions](nuget-acquisition.md)
* [NPM installation instructions](npm-acquisition.md)
* [Cake Build installation instructions](build-systems/cake.md)
* [Rust and Cargo](ecosystems/rust.md)
* [Go](ecosystems/go.md)
* [Java, Maven, and Gradle](ecosystems/java.md)

The non-.NET adapters are not yet claimed to be available from their ecosystem
registries. Follow their source acquisition instructions and the
[ecosystem release status](ecosystem-releases.md).

You must also create [a `version.json` file](versionJson.md) in your repo. See [migration notes](migrating.md) if your repo already has a `version.txt` or `version.json` file from using another system.

## Adopt a versioning workflow

Read the [recommended versioning workflow](versioning-workflow.md) for guidance on when to update `version.json`, how to stabilize and tag releases, and how to maintain servicing branches.

## How to leverage version stamping and runtime information

See relevant documentation for any of these topics:

* [.NET](ecosystems/dotnet.md)
* [Node](ecosystems/node.md)
* [Python](ecosystems/python.md)
* [VSIX](ecosystems/vsix.md)
* [Rust](ecosystems/rust.md)
* [Go](ecosystems/go.md)
* [Java](ecosystems/java.md)

## Build

We have docs to describe how to build with Nerdbank.GitVersioning
for these build systems:

* [MSBuild](build-systems/msbuild.md)
* [gulp](build-systems/gulp.md)
* [Cake Build](build-systems/cake.md)

Also some special [cloud build considerations](cloudbuild.md) (e.g. Azure Pipelines, GitHub Actions, etc.).
