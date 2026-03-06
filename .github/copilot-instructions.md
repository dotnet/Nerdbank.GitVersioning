# Copilot instructions for this repository

## High level guidance

* Review the `CONTRIBUTING.md` file for instructions to build and test the software.
* Run the `.github/Prime-ForCopilot.ps1` script (once) before running any `dotnet` or `msbuild` commands.
  If you see any build errors about not finding git objects or a shallow clone, it may be time to run this script again.

## Software Design

* Design APIs to be highly testable, and all functionality should be tested.
* Avoid introducing binary breaking changes in public APIs of projects under `src` unless their project files have `IsPackable` set to `false`.

## Testing

**IMPORTANT**: This repository uses Microsoft.Testing.Platform (MTP v2) with xunit v3. Traditional `--filter` syntax does NOT work. Use the options below instead.

* There should generally be one test project (under the `test` directory) per shipping project (under the `src` directory). Test projects are named after the project being tested with a `.Tests` suffix.
* Tests use xunit v3 with Microsoft.Testing.Platform (MTP v2). Traditional VSTest `--filter` syntax does NOT work.
* Some tests are known to be unstable. When running tests, you should skip the unstable ones by using `-- --filter-not-trait "FailsInCloudTest=true"`.

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
dotnet test --project test/Library.Tests/Library.Tests.csproj --no-build -c Release -- --filter-method ClassName.MethodName
```

**Run all tests in a test class**:
```bash
dotnet test --project test/Library.Tests/Library.Tests.csproj --no-build -c Release -- --filter-class ClassName
```

**Run tests with wildcard matching** (supports wildcards at beginning and/or end):
```bash
dotnet test --project test/Library.Tests/Library.Tests.csproj --no-build -c Release -- --filter-method "*Pattern*"
```

**Run tests with a specific trait** (equivalent to category filtering):
```bash
dotnet test --project test/Library.Tests/Library.Tests.csproj --no-build -c Release -- --filter-trait "TraitName=value"
```

**Exclude tests with a specific trait** (skip unstable tests):
```bash
dotnet test --project test/Library.Tests/Library.Tests.csproj --no-build -c Release -- --filter-not-trait "TestCategory=FailsInCloudTest"
```

**Run tests for a specific framework only**:
```bash
dotnet test --project test/Library.Tests/Library.Tests.csproj --no-build -c Release --framework net9.0
```

**List all available tests without running them**:
```bash
cd test/Library.Tests
dotnet run --no-build -c Release --framework net9.0 -- --list-tests
```

**Key points about test filtering with MTP v2 / xunit v3**:
- Options after `--` are passed to the test runner, not to `dotnet test`
- Use `--filter-method`, `--filter-class`, `--filter-namespace` for simple filtering
- Use `--filter-trait` and `--filter-not-trait` for trait-based filtering (replaces `--filter "TestCategory=..."`)
- Traditional VSTest `--filter` expressions do NOT work
- Wildcards `*` are supported at the beginning and/or end of filter values
- Multiple simple filters of the same type use OR logic, different types combine with AND
- See `--help` for query filter language for advanced scenarios

## Coding style

* Honor StyleCop rules and fix any reported build warnings *after* getting tests to pass.
* In C# files, use namespace *statements* instead of namespace *blocks* for all new files.
* Add API doc comments to all new public and internal members.
