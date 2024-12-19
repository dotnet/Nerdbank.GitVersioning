---
_layout: landing
---

# Overview

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

[Get started](docs/getting-started.md)
