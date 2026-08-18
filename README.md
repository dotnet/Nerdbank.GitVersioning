# Nerdbank.GitVersioning

[![🏭 Build](https://github.com/dotnet/Nerdbank.GitVersioning/actions/workflows/build.yml/badge.svg)](https://github.com/dotnet/Nerdbank.GitVersioning/actions/workflows/build.yml)
[![NuGet package](https://img.shields.io/nuget/v/Nerdbank.GitVersioning.svg)](https://nuget.org/packages/Nerdbank.GitVersioning)
[![NuGet downloads](https://img.shields.io/nuget/dt/Nerdbank.GitVersioning.svg)](https://nuget.org/packages/Nerdbank.GitVersioning)
[![NPM package](https://img.shields.io/npm/v/nerdbank-gitversioning.svg)](https://www.npmjs.com/package/nerdbank-gitversioning)
[![Join the chat at https://gitter.im/AArnott/Nerdbank.GitVersioning](https://badges.gitter.im/Join%20Chat.svg)](https://gitter.im/AArnott/Nerdbank.GitVersioning?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge)

Stamp your assemblies, packages and more with a unique version generated from a single, simple version.json file and include git commit IDs for non-official builds.

## Features

* Ensure unique versions generated for every git commit that conform to semantic versioning.
* Optional CLI tool calculates versions and backtracks from a computed version to its source git commit.
* MSBuild and NPM integration to automatically stamp various packaging and executables with the computed version. MSBuild integration includes `ThisAssembly` class generation that provides your code with runtime access to all kinds of version information, strong name keys, etc.
* Gives you control over the base version number, placement for the git 'height' incrementing integer, and the prerelease label identifiers via a version.json file.
* Update cloud build names with version number for easy correlation.
* Cross platform. Runs everywhere .NET runs.

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
5. This project is supported by the [.NET Foundation](https://dotnetfoundation.org).

Check out our [getting started documentation](https://dotnet.github.io/Nerdbank.GitVersioning/docs/getting-started.html), then follow the [recommended versioning workflow](https://dotnet.github.io/Nerdbank.GitVersioning/docs/versioning-workflow.html) for releases and servicing.

## Rust library and CLI

The [Rust workspace](src/nerdbank-gitversioning-rs) provides the
`nerdbank-gitversioning` library and a native `nbgv` command-line tool. It is
tested with current stable Rust on Windows, Linux, and macOS. For example:

```rust
use nerdbank_gitversioning::{GitContext, GitEngine, VersionOracle};

fn main() -> Result<(), nerdbank_gitversioning::Error> {
    let context = GitContext::create(".", None, GitEngine::ReadOnly)?;
    let version = VersionOracle::new(&context, None)?.sem_ver2();
    println!("{version}");
    Ok(())
}
```

```console
cargo install --path src/nerdbank-gitversioning-rs/nbgv
nbgv get-version
```

The Rust CLI includes `get-version`, `set-version`, `tag`, `get-commits`,
`cloud`, and `prepare-release`. Normal Git operations run in-process through
libgit2, so a `git` executable is not required. When `commit.gpgSign` is
enabled, `prepare-release` falls back to the `git` executable on `PATH` to
create signed commits and merge commits.

The Rust implementation intentionally excludes the .NET CLI's `install` and
`path-filters` commands and does not provide MSBuild props, targets, tasks,
generated assembly metadata, or package installation. Use the .NET packages
and CLI for those features. See the [Rust README](src/nerdbank-gitversioning-rs/README.md)
and [CLI documentation](https://dotnet.github.io/Nerdbank.GitVersioning/docs/nbgv-cli.html)
for details.

## Code of Conduct

This project has adopted the code of conduct defined by the Contributor Covenant to clarify expected behavior in our community.
For more information see the [.NET Foundation Code of Conduct](https://dotnetfoundation.org/code-of-conduct).

 [semver]: http://semver.org
 [GitHeightMinimum]: https://github.com/dotnet/Nerdbank.GitVersioning/issues/102#issuecomment-269591960
