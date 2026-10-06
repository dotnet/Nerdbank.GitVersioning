# Copilot instructions for this repository

## High level guidance

* Review the `CONTRIBUTING.md` file for instructions to build and test the software.
* Run the `.github/Prime-ForCopilot.ps1` script (once) before running any `dotnet` or `msbuild` commands.
  If you see any build errors about not finding git objects or a shallow clone, it may be time to run this script again.

## Building and Testing

### Initial Setup
1. Run the initialization script: `./init.ps1` or `pwsh ./init.ps1`
2. This downloads NuGet.exe, restores packages, and sets up the build environment

### Building
* For a complete build: `./build.ps1` or `pwsh ./build.ps1`
* Ensure `NBGV_GitEngine=Disabled` is set before building
* The build process first creates NuGet packages, then builds NPM packages

### Testing
* Run all tests the way CI does: `tools/dotnet-test-cloud.ps1 -Configuration Release` (after building)
* That script excludes unstable tests that are known to fail in cloud environments, using each test project's filter syntax
* Tests use the TUnit testing framework, with xUnit assertions (`xunit.v3.assert`).
  The exception is `test/Nerdbank.GitVersioning.Combinatorial.Tests`, which uses xunit v3 because its tests use Xunit.Combinatorial's `[PairwiseData]`.
* All tests should pass when `NBGV_GitEngine=Disabled` is set

## Software Design

* Design APIs to be highly testable, and all functionality should be tested.
* Avoid introducing binary breaking changes in public APIs of projects under `src` unless their project files have `IsPackable` set to `false`.
* Follow existing patterns in the codebase for consistency.

## Testing Guidelines

**IMPORTANT**: This repository uses TUnit with Microsoft.Testing.Platform (MTP v2). Traditional `--filter` syntax does NOT work. Use the options below instead.

The one exception is `test/Nerdbank.GitVersioning.Combinatorial.Tests`, an xunit v3 (MTP v2) project for the build integration tests that use Xunit.Combinatorial's `[PairwiseData]`. TUnit has no pairwise data source, so those tests stay on xunit for now.
It links the shared test infrastructure (`BuildIntegrationTests.cs`, `RepoTestBase.cs`, etc.) from `test/Nerdbank.GitVersioning.Tests`, so keep that infrastructure free of TUnit-specific code. TUnit tests declared on `BuildIntegrationTests` go in `BuildIntegrationTests.Tests.cs`.
Because TUnit and xunit take different filter options, don't pass `--treenode-filter` to `dotnet test` for the whole solution: the xunit project rejects it. Run that project with xunit's options instead (e.g. `--filter-method`, `--filter-class`, `--filter-not-trait`).

* There should generally be one test project (under the `test` directory) per shipping project (under the `src` directory). Test projects are named after the project being tested with a `.Tests` suffix.
* Tests use TUnit with Microsoft.Testing.Platform (MTP v2), while retaining xUnit assertions. Traditional VSTest `--filter` syntax does NOT work.
* Some tests are known to be unstable. Mark them with `[Category("FailsInCloudTest")]` (or `[Trait("Category", "FailsInCloudTest")]` in the xunit project), and skip them when running tests by using `-- --treenode-filter "/**[Category!=FailsInCloudTest]"` (or `-- --filter-not-trait "Category=FailsInCloudTest"` in the xunit project). `tools/dotnet-test-cloud.ps1` reads each test project's filter from its `CloudTestFilterOption` and `CloudTestFilterValue` MSBuild properties.
* Write tests that cover both happy path and edge cases.
* Ensure all new functionality is covered by tests.

### Running Tests

**Run all tests**:
```bash
dotnet test --no-build -c Release
```

**Run the xunit (Xunit.Combinatorial) tests**:
```bash
dotnet test --project test/Nerdbank.GitVersioning.Combinatorial.Tests/Nerdbank.GitVersioning.Combinatorial.Tests.csproj --no-build -c Release -- --filter-method "*BuildNumber_VariousOptions"
```

**Run tests for a specific test project**:
```bash
dotnet test --project test/Nerdbank.GitVersioning.Tests/Nerdbank.GitVersioning.Tests.csproj --no-build -c Release
```

**Run a single test method**:
```bash
dotnet test --project test/Nerdbank.GitVersioning.Tests/Nerdbank.GitVersioning.Tests.csproj --no-build -c Release -- --treenode-filter "/*/*/ClassName/MethodName"
```

**Run all tests in a test class**:
```bash
dotnet test --project test/Nerdbank.GitVersioning.Tests/Nerdbank.GitVersioning.Tests.csproj --no-build -c Release -- --treenode-filter "/*/*/ClassName/*"
```

**Run tests with wildcard matching** (supports wildcards at beginning and/or end):
```bash
dotnet test --project test/Nerdbank.GitVersioning.Tests/Nerdbank.GitVersioning.Tests.csproj --no-build -c Release -- --treenode-filter "/*/*/*/*Pattern*"
```

**Run tests with a specific property**:
```bash
dotnet test --project test/Nerdbank.GitVersioning.Tests/Nerdbank.GitVersioning.Tests.csproj --no-build -c Release -- --treenode-filter "/*/*/*/*[PropertyName=value]"
```

**Exclude tests with a specific category** (skip unstable tests):
```bash
dotnet test --project test/Nerdbank.GitVersioning.Tests/Nerdbank.GitVersioning.Tests.csproj --no-build -c Release -- --treenode-filter "/**[Category!=FailsInCloudTest]"
```

**Run tests for a specific framework only**:
```bash
dotnet test --project test/Nerdbank.GitVersioning.Tests/Nerdbank.GitVersioning.Tests.csproj --no-build -c Release --framework net10.0
```

**List all available tests without running them**:
```bash
cd test/Nerdbank.GitVersioning.Tests
dotnet run --no-build -c Release --framework net10.0 -- --list-tests
```

**Key points about test filtering with TUnit / MTP v2**:
- Options after `--` are passed to the test runner, not to `dotnet test`
- Use `--treenode-filter` to select tests by assembly, namespace, class, method, or property
- Traditional VSTest `--filter` expressions do NOT work
- Wildcards `*` are supported in tree node segments
- See `--help` for query filter language for advanced scenarios

## Coding Style

* Honor StyleCop rules and fix any reported build warnings *after* getting tests to pass.
* In C# files, use namespace *statements* instead of namespace *blocks* for all new files.
* Add API doc comments to all new public and internal members in shipping code under `src`. Tests and samples do not require XML API documentation; do not add XML docs to test or sample members solely to satisfy this rule.
* Follow existing code formatting and naming conventions in the repository.
* Use meaningful variable and method names that clearly express intent.
