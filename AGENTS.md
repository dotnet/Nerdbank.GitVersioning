# Copilot instructions for this repository

## High level guidance

* Review the `CONTRIBUTING.md` file for instructions to build and test the software.
* Run the `.github/Prime-ForCopilot.ps1` script (once) before running any `dotnet` or `msbuild` commands.
  If you see any build errors about not finding git objects or a shallow clone, it may be time to run this script again.

## Software Design

* Design APIs to be highly testable, and all functionality should be tested.
* Avoid introducing binary breaking changes in public APIs of projects under `src` unless their project files have `IsPackable` set to `false`.

## Testing

**IMPORTANT**: This repository uses TUnit with Microsoft.Testing.Platform (MTP v2). Traditional `--filter` syntax does NOT work. Use the options below instead.

* There should generally be one test project (under the `test` directory) per shipping project (under the `src` directory). Test projects are named after the project being tested with a `.Tests` suffix.
* Tests use TUnit with Microsoft.Testing.Platform (MTP v2), while retaining xUnit assertions. Traditional VSTest `--filter` syntax does NOT work.

### Running Tests

**Run all tests**:
```bash
dotnet test --no-build -c Release
```

**Run tests for a specific test project**:
```bash
dotnet test --project test/Library.Tests/Library.Tests.csproj --no-build -c Release
```

**Run a single test method**:
```bash
dotnet test --project test/Library.Tests/Library.Tests.csproj --no-build -c Release -- --treenode-filter "/*/*/ClassName/MethodName"
```

**Run all tests in a test class**:
```bash
dotnet test --project test/Library.Tests/Library.Tests.csproj --no-build -c Release -- --treenode-filter "/*/*/ClassName/*"
```

**Run tests with wildcard matching** (supports wildcards at beginning and/or end):
```bash
dotnet test --project test/Library.Tests/Library.Tests.csproj --no-build -c Release -- --treenode-filter "/*/*/*/*Pattern*"
```

**Run tests with a specific property**:
```bash
dotnet test --project test/Library.Tests/Library.Tests.csproj --no-build -c Release -- --treenode-filter "/*/*/*/*[PropertyName=value]"
```

**Run tests for a specific framework only**:
```bash
dotnet test --project test/Library.Tests/Library.Tests.csproj --no-build -c Release --framework net8.0
```

**List all available tests without running them**:
```bash
cd test/Library.Tests
dotnet run --no-build -c Release --framework net8.0 -- --list-tests
```

**Key points about test filtering with TUnit / MTP v2**:
- Options after `--` are passed to the test runner, not to `dotnet test`
- Use `--treenode-filter` to select tests by assembly, namespace, class, method, or property
- Traditional VSTest `--filter` expressions do NOT work
- Wildcards `*` are supported in tree node segments
- See `--help` for query filter language for advanced scenarios

## Coding style

* Honor StyleCop rules and fix any reported build warnings *after* getting tests to pass.
* In C# files, use namespace *statements* instead of namespace *blocks* for all new files.
* Add API doc comments to all new public and internal members in shipping code under `src`. Tests and samples do not require XML API documentation; do not add XML docs to test or sample members solely to satisfy this rule.
