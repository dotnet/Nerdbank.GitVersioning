# Copilot instructions for this repository

## High level guidance

* Review the `CONTRIBUTING.md` file for instructions to build and test the software.
* Set the `NBGV_GitEngine` environment variable to `Disabled` before running any `dotnet` or `msbuild` commands.

## Environment Setup and Shallow Clone Workaround

This repository uses Nerdbank.GitVersioning which depends on git history for version calculation. Since GitHub Copilot Coding Agent operates on shallow clones, you MUST set the following environment variable before running any build commands:

```bash
export NBGV_GitEngine=Disabled
```

**Important**: The environment variable name and value are case-sensitive. This setting:
- Removes access to git history during builds
- Allows builds to succeed in shallow clone environments  
- Results in incorrect version stamps (expected in Copilot environment)
- Still generates the `ThisAssembly` class but with fewer properties

## Building and Testing

### Initial Setup
1. Run the initialization script: `./init.ps1` or `pwsh ./init.ps1`
2. This downloads NuGet.exe, restores packages, and sets up the build environment

### Building
* For a complete build: `./build.ps1` or `pwsh ./build.ps1`
* Ensure `NBGV_GitEngine=Disabled` is set before building
* The build process first creates NuGet packages, then builds NPM packages

### Testing
* Run all tests: `dotnet test --filter "TestCategory!=FailsInCloudTest"`
* The filter excludes unstable tests that are known to fail in cloud environments
* Tests use the Xunit testing framework
* All tests should pass when `NBGV_GitEngine=Disabled` is set

## Software Design

* Design APIs to be highly testable, and all functionality should be tested.
* Avoid introducing binary breaking changes in public APIs of projects under `src` unless their project files have `IsPackable` set to `false`.
* Follow existing patterns in the codebase for consistency.

## Testing Guidelines

* There should generally be one test project (under the `test` directory) per shipping project (under the `src` directory). Test projects are named after the project being tested with a `.Test` suffix.
* Tests should use the Xunit testing framework.
* Some tests are known to be unstable. When running tests, you should skip the unstable ones by running `dotnet test --filter "TestCategory!=FailsInCloudTest"`.
* Write tests that cover both happy path and edge cases.
* Ensure all new functionality is covered by tests.

## Coding Style

* Honor StyleCop rules and fix any reported build warnings *after* getting tests to pass.
* In C# files, use namespace *statements* instead of namespace *blocks* for all new files.
* Add API doc comments to all new public and internal members.
* Follow existing code formatting and naming conventions in the repository.
* Use meaningful variable and method names that clearly express intent.
