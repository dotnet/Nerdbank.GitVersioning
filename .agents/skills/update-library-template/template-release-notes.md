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

## NativeAOT compatibility validation

Shipping projects now set `IsAotCompatible` for target frameworks compatible with .NET 8.
Apply the same conditional property to each shipping project that is intended to support NativeAOT, then resolve all trim and AOT analyzer warnings rather than suppressing them broadly.

The new `test/AotCompatibilityTest` project catches NativeAOT compiler warnings that library analyzers may not report.
Keep it in the solution, add every NativeAOT-compatible shipping assembly as a `TrimmerRootAssembly`, and retain its publishable entry in `test/dirs.proj`.
CI validates this project through the existing traversal publish on Windows, Linux, and macOS; it is intentionally marked `IsTestProject=false` because publishing successfully is the test.

## NativeAOT opt-out and AI merge guidance

Not every repo wants to take on NativeAOT readiness or NativeAOT-published test validation.
If a receiving repo does not intend to support NativeAOT, opt out explicitly instead of inheriting the template's defaults.

For test projects that should remain managed-only:

* Set `<PublishNativeAOTTests>false</PublishNativeAOTTests>` in the project file or in a repo-level `Directory.Build.props`.
* Leave the managed test project in the solution so usual IL-based testing continues to work.
* Keep `dotnet test` and the managed coverage flow unchanged; only the NativeAOT publish step is disabled.

For shipping libraries that do not want NativeAOT compatibility analysis:

* Remove `IsAotCompatible` from the project, or set it only for specific target frameworks that truly support NativeAOT.
* Do not add new AOT compatibility warnings to the build if the library is not intended to be AOT-compatible.
* If the repo does not target .NET 8 or later, the property should be omitted entirely rather than applied unconditionally.

For the compatibility project itself:

* Remove the `test/AotCompatibilityTest` project from the solution if NativeAOT validation is out of scope.
* Remove its entry from `test/dirs.proj` so the traversal publish no longer publishes it.
* Keep the project only when the repo intends to validate shipping assemblies under NativeAOT in CI.

When merging this template into a downstream repo, the AI assistant should ask whether NativeAOT is a relevant target for that repo before applying these changes if `IsAotCompatible` is not already set by any of the projects.
If the answer is no, it should apply the opt-out steps above instead of blindly keeping the NativeAOT defaults.
This is especially important for repos that are not shipping a NativeAOT-ready library or that cannot support the extra publish-time validation in CI.
If the repo owner is unsure about NativeAOT support, you might offer to test the repo for NativeAOT readiness by attempting a NativeAOT publish of the relevant projects and observing whether any compatibility warnings or errors are produced.

In the event the user elects for NativeAOT targeting, make efforts to get tests to work under NativeAOT as well so the shipping code can confidently advertise its NativeAOT compatibility.
If aspects of the library or tests make successful builds, publish or test runs problematic, discuss this with the repo owner before making significant changes or giving up on NativeAOT support.
