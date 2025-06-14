# Contributing to this project

This project has adopted the code of conduct defined by the Contributor Covenant to clarify expected behavior in our community.
For more information see the [.NET Foundation Code of Conduct](https://dotnetfoundation.org/code-of-conduct).

## Prerequisites

This project is actively developed using the following software.
It is highly recommended that anyone contributing to this library use the same
software.

1. [Visual Studio 2022][VS]
2. [Node.js][NodeJs] v16 (v18 breaks our build)

### Optional additional software

Some projects in the Visual Studio solution require optional or 3rd party components to open.
They are not required to build the full solution from the command line using MSBuild,
but installing this software will facilitate an enhanced developer experience in Visual Studio.

1. [Node.js Tools for Visual Studio][NodeJsTools]

All other dependencies are acquired via NuGet or NPM.

## Building

To build this repository from the command line, you must first execute our init.ps1 script,
which downloads NuGet.exe and uses it to restore packages.
Assuming your working directory is the root directory of this git repo,
and you are running Windows PowerShell, the command is:

    .\init.ps1

Most of the repo may be built via building the solution file from Visual Studio 2019,
but for a complete build, build from the VS2019 Developer Command Prompt:

    .\build.ps1

This repo is structured such that it builds the NuGet package first, using MSBuild.
It then builds an NPM package that includes some of the outputs of MSBuild, along with
some javascript, for our NPM consumers who want a reasonable versioning story for their
NPM packages too.

## Testing

`dotnet test` will run all tests.

The Visual Studio 2022 Test Explorer will list and execute all tests.

A few tests will fail without a certain VC++ toolset installed.

## Releases

Use `nbgv tag` to create a tag for a particular commit that you mean to release.
[Learn more about `nbgv` and its `tag` and `prepare-release` commands](https://dotnet.github.io/Nerdbank.GitVersioning/docs/nbgv-cli.html).

Push the tag.

### GitHub Actions

When your repo is hosted by GitHub and you are using GitHub Actions, you should create a GitHub Release using the standard GitHub UI.
Having previously used `nbgv tag` and pushing the tag will help you identify the precise commit and name to use for this release.

After publishing the release, the `.github\workflows\release.yml` workflow will be automatically triggered, which will:

1. Find the most recent `.github\workflows\build.yml` GitHub workflow run of the tagged release.
1. Upload the `deployables` artifact from that workflow run to your GitHub Release.
1. If you have `NUGET_API_KEY` defined as a secret variable for your repo or org, any nuget packages in the `deployables` artifact will be pushed to nuget.org.

### Azure Pipelines

When your repo builds with Azure Pipelines, use the `azure-pipelines/release.yml` pipeline.
Trigger the pipeline by adding the `auto-release` tag on a run of your main `azure-pipelines.yml` pipeline.

## Tutorial and API documentation

API and hand-written docs are found under the `docfx/` directory. and are built by [docfx](https://dotnet.github.io/docfx/).

You can make changes and host the site locally to preview them by switching to that directory and running the `dotnet docfx --serve` command.
After making a change, you can rebuild the docs site while the localhost server is running by running `dotnet docfx` again from a separate terminal.

The `.github/workflows/docs.yml` GitHub Actions workflow publishes the content of these docs to github.io if the workflow itself and [GitHub Pages is enabled for your repository](https://docs.github.com/en/pages/quickstart).

## Pull requests

Pull requests are welcome! They may contain additional test cases (e.g. to demonstrate a failure),
and/or product changes (with bug fixes or features). All product changes should be accompanied by
additional tests to cover and justify the product change unless the product change is strictly an
efficiency improvement and no outwardly observable change is expected.

In the master branch, all tests should always pass. Added tests that fail should be marked as Skip
via `[Fact(Skip = "Test does not pass yet")]` or similar message to keep our test pass rate at 100%.

 [VS]: https://www.visualstudio.com/downloads/
 [NodeJs]: https://nodejs.org
 [NodeJsTools]: https://www.visualstudio.com/vs/node-js/

## Updating dependencies

This repo uses Renovate to keep dependencies current.
Configuration is in the `.github/renovate.json` file.
[Learn more about configuring Renovate](https://docs.renovatebot.com/configuration-options/).

When changing the renovate.json file, follow [these validation steps](https://docs.renovatebot.com/config-validation/).

If Renovate is not creating pull requests when you expect it to, check that the [Renovate GitHub App](https://github.com/apps/renovate) is configured for your account or repo.

## Merging latest from Library.Template

### Maintaining your repo based on this template

The best way to keep your repo in sync with Library.Template's evolving features and best practices is to periodically merge the template into your repo:
`
```ps1
git fetch
git checkout origin/main
.\tools\MergeFrom-Template.ps1
# resolve any conflicts, then commit the merge commit.
git push origin -u HEAD
```
