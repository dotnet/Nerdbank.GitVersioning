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
dotnet tool install --tool-path . nbgv
```

At this point you can launch the tool using `./nbgv` in your build script.

## Preparing a release

The `prepare-release` helps with creating a  release branchs.
The command assumes you are the following branching model:

- There is a main branch (typically `master`) that contains the latest version
  of your software. Builds of the main branch alwas use a prerelease tag.
- For releases, separate release branches are created.
  Builds from release branches use either no or a different prerelease tag than
  the main branch.
- Fixes made on a release branch are brought back to the main branch either
  by merging the branch or by cherry-picking the changes.

The `prepare-release` command supports this working model by taking care of
creating the release branch and updating `version.json` on both branches.

To prepare a release, run

```ps1
nbgv prepare-release
```

This will

- Read the version on the current branch
- Create a new release branch for the version. If the version on the current
  branch is e.g. `1.2-beta`, a release branch named `release/v1.2` will be
  created.
- Remove the prerelease tag from `version.json` on the release branch
- Increment the version and set the prerelease tag on the main branch.
  By default, `prepare-release` will increment the minor version and set the
  prerelease tag to `alpha`. If the version has multiple prerelease tags
  (separated by '.'), only the first tag will be updated.
  In the above example, the version on the main branch would be set to `1.3-alpha`.
- Merge the release brach back to the main branch resolving the conflict in `version.json`.
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
in the current branch by setting/removing the prerelease tag.

### Explicitly setting the next version

If you want to explicitly set the next version of the main branch instead of
automatically determining it by incrementing the current version, you
can set the version as commandline parameter:

```ps1
nbgv prepare-release --nextVersion 2.0-beta
```

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

| Property         | Default value        | Description                                                                                                                                |
|------------------|----------------------|--------------------------------------------------------------------------------------------------------------------------------------------|
| branchName       | `v{version}` | Defines the format of release branch names. The value must include a `{version}` placeholder.                                              |
| versionIncremnt  | `minor`              | Specifies which part of the version on the current branch is incremented when preparing a release. Allowed values are `minor` and `major`. |
| firstUnstableTag | `alpha`              | Specified the unstable tag to use for the main branch.                                                                                     |

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
