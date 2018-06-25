# Using the nbgv .NET Core CLI tool

Perform a one-time install of the `nbgv` tool using the following dotnet CLI command:

```cmd
dotnet tool install -g nbgv
```

You may then use the `nbgv` tool to install Nerdbank.GitVersioning into your repos, as well as query and update version information for your repos and projects.

Install Nerdbank.GitVersioning into your repo using this command from within your repo:

```cmd
nbgv install
```

This will create your initial `version.json` file.
It will also add/modify your `Directory.Build.props` file in the root of your repo to add the `PackageReference` to the latest `Nerdbank.GitVersioning` package available on nuget.org.

## Learn more

There are several more sub-commands and switches to each to help you build and maintain your projects, find a commit that built a particular version later on, create tags, etc.

Use the `--help` switch on the `nbgv` command or one of its sub-commands to learn about the sub-commands available and how to use them. For example, this is the basic usage help text:

```cmd
nbgv --help
usage: nbgv <command> [<args>]

    install        Prepares a project to have version stamps applied
                   using Nerdbank.GitVersioning.
    get-version    Gets the version information for a project.
    set-version    Updates the version stamp that is applied to a
                   project.
    tag            Creates a git tag to mark a version.
    get-commits    Gets the commit(s) that match a given version.
    cloud          Communicates with the ambient cloud build to set the
                   build number and/or other cloud build variables.
```
