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
5. This project is supported by the [.NET Foundation](https://dotnetfoundation.org).

Check out our [getting started documentation](https://dotnet.github.io/Nerdbank.GitVersioning/docs/getting-started.html).

## Code of Conduct

This project has adopted the code of conduct defined by the Contributor Covenant to clarify expected behavior in our community.
For more information see the [.NET Foundation Code of Conduct](https://dotnetfoundation.org/code-of-conduct).

 [semver]: http://semver.org
 [GitHeightMinimum]: https://github.com/dotnet/Nerdbank.GitVersioning/issues/102#issuecomment-269591960
