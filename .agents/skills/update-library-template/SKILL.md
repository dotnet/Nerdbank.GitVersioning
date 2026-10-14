---
name: update-library-template
description: Merges the latest Library.Template into this repo (at position of HEAD) and resolves conflicts.
disable-model-invocation: true
---

# Instructions

1. Run `./tools/MergeFrom-Template.ps1` from the repo root.
2. Resolve merge conflicts, taking into account conflict resolution policy below.
3. Validate the changes, as described in the validation section below.
4. Committing your changes (if applicable).

## Conflict resolution policy

There may be [special notes](template-release-notes.md) that describe special considerations for certain files or scenarios to help you resolve conflicts appropriately.
Always refer to that file before proceeding.
In particular, focus on the *incoming* part of the file, since it represents the changes from the Library.Template that you are merging into your repo.

Also consider that some repos choose to reject certain Library.Template patterns.
For example the template uses MTPv2 for test projects, but a repo might have chosen not to adopt that.
When resolving merge conflicts, consider whether it looks like the relevant code file is older than it should be given the changes the template is bringing in.
Ask the user when in doubt as to whether the conflict should be resolved in favor of 'catching up' with the template or keeping the current changes.

Use #runSubagent to analyze and resolve merge conflicts across files in parallel.

### Keep Current files

Conflicts in the following files should always be resolved by keeping the current version (i.e. discard incoming changes):

* README.md

### Test framework docs, scripts, and packages

Library.Template's `AGENTS.md`, test docs, and `tools/dotnet-test-cloud.ps1` assume **TUnit** on Microsoft.Testing.Platform.
Many consumers still use **xunit v3 + MTP**, classic **VSTest xunit**, or hybrid setups (including xunit *extension* libraries such as Xunit.StaFact / Xunit.SkippableFact / Xunit.Combinatorial, whose own tests must stay on xunit).

When merging:

* Prefer the template only where the consumer's prior content was **equivalent**. Do not overwrite repo-specific behavior with template placeholders or TUnit-only defaults.
* Keep framework-specific **AGENTS.md** (and similar agent/contributor docs): filter syntax (`--filter-method` / `--filter-not-trait` vs `--treenode-filter`), FailsInCloudTest / FailureExpected exclusions, real test project paths, and `--framework` TFMs that match the repo's test projects.
* Keep bespoke **`tools/dotnet-test-cloud.ps1`** logic when it is not equivalent to the template (per-project loops, NonTUnit vs TUnit splits, MultiRID/NativeAOT discovery, hang/blame timeouts, coverage naming, custom filters). Graft genuine template improvements (for example `IncludeNativeAOT` + `Get-NativeAOTTestProjects.ps1`) onto the repo script only when they fit.
* Do not add unused template **PackageVersion** entries (`TUnit.Engine`, `xunit.v3.assert.aot`, etc.) to `Directory.Packages.props` unless a project in the repo actually references them (or Central Package Management truly requires them).
* Preserve repo-specific Azure Pipelines / GitHub Actions build steps that the template lacks equivalents for (for example full **MSBuild@1** on Windows for `net35`, `IncompleteBuild` as `warnNotAsError`).

### Traversal coverage, placeholders, and non-test projects

* `GitVersionBaseDirectory` in the template's `Directory.Build.props` assumes the repo has exactly one `version.json`. If the repo has nested `version.json` files (typically for analyzer or source generator projects that need `assemblyVersion` precision `revision`), the repo has deliberately removed or must not have that property. Never re-add it during a merge; keep the repo's first-parent state.
* `init.ps1` and CI restore and build only `tools/dirs.proj`. If the solution contains projects outside `src` and `test` (for example `samples/` or `benchmark/`), add them to `tools/dirs.proj` (with `Pack="false"` and/or `Publish="false"` as appropriate) so they are still restored and compiled. Otherwise `dotnet format --no-restore` and sample regressions go unnoticed.
* `test/Directory.Build.props` marks every project under `test` as a test project. Set `<IsTestProject>false</IsTestProject>` on non-test executables there (for example BenchmarkDotNet projects) so they are not run by `dotnet test` or picked up by NativeAOT test discovery.
* After the merge, search `CONTRIBUTING.md`, `AGENTS.md`, and source headers for template placeholders such as `test/Library.Tests/Library.Tests.csproj`, the company-name placeholder token that `Expand-Template.ps1` replaces with the author, and the template's `net8.0` examples, and replace them with the repo's real project paths, metadata, and NativeAOT-enabled frameworks.
* If the repo already publishes NativeAOT tests through its own mechanism (for example `MultiRIDProjectReference` items in `test/dirs.proj`), either keep that mechanism and disable the template's discovery (`PublishNativeAOTTests=false`, and drop or don't wire up `Get-NativeAOTTestProjects.ps1`), or replace it fully. Don't leave both half-wired, and update `CONTRIBUTING.md` to describe whichever one the repo actually uses.


### Deleted files

Very typically, when the incoming change is to a file that was deleted locally, the correct resolution is to re-delete the file.

In some cases however, the deleted file may have incoming changes that should be applied to other files.
The `test/Library.Tests/Library.Tests.csproj` file is very typical of this.
Changes to this file should very typically be applied to any and all test projects in the repo.
You are responsible for doing this in addition to re-deleting this template file.

## Validation

Validate the merge result (after resolving any conflicts, if applicable).
Use #runSubagent for each step.

1. Verify that `dotnet restore` succeeds. Fix any issues that come up.
2. Verify that `dotnet build` succeeds.
3. Verify that tests succeed by running `tools/dotnet-test-cloud.ps1`.

While these validations are described using `dotnet` CLI commands, some repos require using full msbuild.exe.
You can detect this by checking the `azure-pipelines/dotnet.yml` or `.github/workflows/build.yml` files for use of one or the other tool.

You are *not* responsible for fixing issues that the merge did not cause.
If validation fails for reasons that seem unrelated to the changes brought in by the merge, advise the user and ask how they'd like you to proceed.
That said, sometimes merges will bring in SDK or dependency updates that can cause breaks in seemingly unrelated areas.
In such cases, you should investigate and solve the issues as needed.

## Committing your changes

If you have to make any changes for validations to pass, consider whether they qualify as a bad merge conflict resolution or more of a novel change that you're making to work with the Library.Template update.
Merge conflict resolution fixes ideally get amended into the merge commit, while novel changes would go into a novel commit after the merge commit.

Always author your commits using `git commit --author "🤖 Copilot <no-reply@github.com>"` (and possibly other parameters).
Describe the nature of the merge conflicts you encountered and how you resolved them in your commit message.

Later, if asked to review pull request validation breaks, always author a fresh commit with each fix that you push, unless the user directs you to do otherwise.
