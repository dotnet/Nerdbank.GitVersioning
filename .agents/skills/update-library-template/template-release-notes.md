# Template release notes

This file will describe significant changes in Library.Template as they are introduced, especially if they require special consideration when merging updates into existing repos.
This file is referenced by update-library-template.prompt.md and should remain in place to facilitate future merges, whether done manually or by AI.

## Solution rename

Never leave a Library.slnx file in the repository.
You might even see one there even though this particular merge didn't bring it in.
This can be an artifact of having renamed Library.sln to Library.slnx in the template repo, but ultimately the receiving repo should have only one .sln or .slnx file, with a better name than `Library`.
Delete any `Library.slnx` that you see.
Migrate an `.sln` in the repo root to `.slnx` using this command:

```ps1
dotnet solution EXISTING.sln migrate
```

This will create an EXISTING.slnx file. `git add` that file, then `git rm` the old `.sln` file.
Sometimes a repo will reference the sln filename in a script or doc somewhere.
Search the repo for such references and update them to the slnx file.

## Migrating from xUnit to TUnit

The template now uses TUnit with Microsoft.Testing.Platform instead of the xUnit test runner.
When merging this update into an existing repository, migrate all test projects and repo-specific test automation.
Follow the [TUnit xUnit migration guide](https://tunit.dev/docs/migration/xunit), including its automated migration code fixes.

* Replace xUnit runner/framework package references with `TUnit.Engine` and set `<OutputType>Exe</OutputType>` in each test project.
  Remove obsolete runner dependencies such as `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, and `coverlet.collector`, along with `xunit.runner.json`.
  Reconcile central package versions with the template's `Directory.Packages.props`.
* Existing xUnit assertions can remain: the template retains `xunit.v3.assert`, using `xunit.v3.assert.aot` for .NET 9 and later.
  Keep `Xunit` imports for assertions, but use TUnit for test attributes and lifecycle APIs.
* Convert `[Fact]` and `[Theory]` to `[Test]`, and `[InlineData]` to `[Arguments]`.
  Migrate member/class data sources, traits, fixtures, collection behavior, setup/teardown, and test output according to the migration guide.
  Review tests that depend on xUnit's execution ordering or parallelization rules; do not assume those rules carry over.
* Merge the `global.json` Microsoft.Testing.Platform runner setting, test directory build files, traversal projects, and updated PowerShell/YAML automation.
  Keep every test project in the repository's solution for managed test runs.
  The traversals discover `.csproj` files under `src` and `test`; adjust discovery if your projects live elsewhere or use another project language.
* Update custom test commands and filters.
  VSTest `--filter` expressions do not work with this runner; pass TUnit filters after `--`, for example `-- --treenode-filter "/*/*/ClassName/MethodName"`.
  Replace any xUnit-specific CI result or coverage handling with the template's Microsoft.Testing.Platform integration.
* Validate managed tests and coverage, then run `dotnet publish tools\dirs.proj -c Release` and `.\tools\dotnet-test-cloud.ps1 -Configuration Release -IncludeNativeAOT`.
  Test projects targeting .NET 8 or later are eligible for NativeAOT publication by default.
  If a project cannot support NativeAOT, set `<PublishNativeAOTTests>false</PublishNativeAOTTests>` in its project file; it will still run as managed tests.
