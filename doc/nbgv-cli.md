# Using the nbgv .NET Core CLI tool

Perform a one-time install of the `nbgv` tool using the following dotnet CLI command:

```ps1
dotnet tool install -g nbgv
```

You may then use the `nbgv` tool to install Nerdbank.GitVersioning into your repos, as well as query and update version information for your repos and projects.

Install Nerdbank.GitVersioning into your repo using this command from within your repo:

```ps1
nbgv install
```

This will create your initial `version.json` file.
It will also add/modify your `Directory.Build.props` file in the root of your repo to add the `PackageReference` to the latest `Nerdbank.GitVersioning` package available on nuget.org.

## CI Builds

If scripting for running in a CI build where global impact from installing a tool is undesirable, you can localize the tool installation:

```ps1
dotnet tool install --tool-path my/path nbgv
```
> Ensure your custom path is outside of your git repository, as the `nbgv` tool doesn't support uncommited changes

At this point you can launch the tool using `./nbgv` in your build script.

## Preparing a release

The `prepare-release` command automates the task of branching off the main development branch to stabilize for an upcoming release. It is optimized for the following workflow:

- There is a branch (typically `master` ) where main development happens.
  This branch typically builds with some `-prerelease` tag.
  It *may* be a "public release" for early prereleases.
- To stabilize for and/or ship a release, a branch named after the version to be shipped is created.
  This branch *may* include a `-prerelease` tag, typically a more advanced tag than any found in `master`. For example, if `master` builds `-alpha` then the stabilization branch would build `-beta` or `-rc`.
- Each release branch may be periodically merged into the next newer release branch or `master` so that hot fixes also ship in the next major release.

The `prepare-release` command supports this working model by taking care of
creating the release branch and updating `version.json` on both branches.

To prepare a release, first ensure there is no uncommited changes in your repository then run:

```ps1
nbgv prepare-release
```

This will:

1. Read `version.json` to ascertain the version under development,
   and the naming convention of release branches.
1. Create a new release branch for that version. If the version on the current
   branch is `1.2-beta` and the release branch naming convention is `release/v{version}`,
   a release branch named `release/v1.2` will be created.
1. Remove the prerelease tag from `version.json` on the release branch.
   Optionally (if an argument is passed to the command) a new prerelease tag is used to replace the old one.
1. Back on the original branch, increment the version as specified in `version.json`.
   By default, `prepare-release` will increment the minor version and set the
   prerelease tag to `alpha`. If the version has multiple prerelease tags
   (separated by '.'), only the first tag will be updated.
   In the above example, the version on the main branch would be set to `1.3-alpha`.
1. Merge the release branch back to the main branch, resolving the conflict in `version.json`.
   This avoids having to resolve the conflict when merging the branch at a later
   time.

You can optionally include a prerelease tag on the release branch, e.g. when
you want to do some stabilization first. This can be achieved by passing a
tag to the command, e.g.:

```ps1
nbgv prepare-release rc
```

**Note:** When the current branch is already the release branch for the current version,
no new branch will be created. Instead the tool will just update the version
in the current branch by replacing or removing the prerelease tag.

### Customizing the next version

By default, the next version of the main branch is determined from the current
version and the `versionIncrement` setting in `version.json`.
To customize this behaviour, you can either explicitly set the next version
or override the version increment setting.

To explicitly set the next version, run:

```ps1
nbgv prepare-release --nextVersion 2.0
```

To override the `versionIncrement` setting from `version.json`, run:

```ps1
nbgv prepare-release --versionIncrement Major
```

**Note:** The parameters `nextVersion` and `versionIncrement` cannot
be combined.

### Customizing the behaviour of `prepare-release`

The behaviour of the `prepare-release` command can be customized in
`version.json`:

```json
{
  "version": "1.0",
  "release": {
    "branchName" : "release/v{version}",
    "versionIncrement" : "minor",
    "firstUnstableTag" : "alpha"
  }
}
```

| Property         | Default value        | Description                                                                                                                                         |
|------------------|----------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------|
| branchName       | `v{version}`         | Defines the format of release branch names. The value must include a `{version}` placeholder.                                                       |
| versionIncrement | `minor`              | Specifies which part of the version on the current branch is incremented when preparing a release. Allowed values are `major`, `minor` and `build`. |
| firstUnstableTag | `alpha`              | Specified the unstable tag to use for the main branch.                                                                                              |

### Customizing the `prepare-release` output format

By default, the `prepare-release` command writes information about created and updated branches to the console as text.
Alternatively the information can be written to the output as `json`.
The output format to use can be set using the `--format` command line parameter.

For example, running the following command on `master`

```
nbgv prepare-release --format json
```

will generate output similar to this:

```json
{
  "CurrentBranch": {
    "Name": "master",
    "Commit": "5a7487098ac1be1ceb4dbf72d862539cf0b0c27a",
    "Version": "1.7-alpha"
  },
  "NewBranch": {
    "Name": "v1.7",
    "Commit": "b2f164675ffe891b66b601c00efc4343581fc8a5",
    "Version": "1.7"
  }
}
```

The JSON object has two properties:

- `CurrentBranch` provides information about the branch that `prepare-release` was started on (typically `master`)
- `NewBranch` provides information about the new branch created by the command.

For each branch, the following properties are provided:

- `Name`: The name of the branch
- `Commit`: The id of the latest commit on that branch
- `Version`: The version configured in that branch's `version.json`

**Note:** When the current branch is already the release branch for the current version, no new branch will be created.
In that case, the `NewBranch` property will be `null`.

### Customizing the `prepare-release` commit message

By default, the `prepare-release` command generates a commit message with the format "Set version to {version}".
A switch allows you to customize the commit message, using `{0}` as a placeholder for the version.

For example, running the following command:

```
nbgv prepare-release --commit-message-pattern "Custom commit message pattern - {0} custom message"
```

So your commit message is going to be this:

```
Custom commit message pattern - 1.0 custom message
```

## Creating a version tag

The `tag` command automates the task of tagging a commit with a version.

To create a version tag, run:

```ps1
nbgv tag
```

This will:

1. Read version.json to ascertain the version under development, and the naming convention of tag names.
1. Create a new tag for that version.

You can optionally include a version or commit id to create a new tag for an older version/commit, e.g.:

```ps1
nbgv tag 1.0.0
```

### Customizing the behaviour of `tag`

The behaviour of the `tag` command can be customized in `version.json`:

```json
{
  "version": "1.0",
  "release": {
    "tagName" : "v{version}"
  }
}
```

| Property | Default value | Description                                                                                                                                         |
|----------|---------------|-------------------------------------------------------------------------------------------------|
| tagName  | `v{version}`  | Defines the format of tag names. Format must include a placeholder '{version}' for the version. |

## Learn more

There are several more sub-commands and switches to each to help you build and maintain your projects, find a commit that built a particular version later on, create tags, etc.

Use the `--help` switch on the `nbgv` command or one of its sub-commands to learn about the sub-commands available and how to use them. For example, this is the basic usage help text:

```
nbgv --help
usage: nbgv <command> [<args>]

    install          Prepares a project to have version stamps applied
                     using Nerdbank.GitVersioning.
    get-version      Gets the version information for a project.
    set-version      Updates the version stamp that is applied to a
                     project.
    tag              Creates a git tag to mark a version.
    get-commits      Gets the commit(s) that match a given version.
    cloud            Communicates with the ambient cloud build to set the
                     build number and/or other cloud build variables.
    prepare-release  Prepares a release by creating a release branch for
                     the current version and adjusting the version on the
                     current branch.
```
