# Path filters

## Problem

Some repositories may contain more than one project. This is sometimes referred to as a _mono repo_ (as opposed to having a repo for each project - _many repo_). Imagine a repository structured as:

- /
  - Foo/
    - version.json => `{"version": "1.0"}`
  - Bar/
    - version.json => `{"version": "2.1"}`
  - Quux/
    - version.json => `{"version": "4.3"}`
  - README.md

With GitVersioning's default configuration, a commit to a given project's subtree will result in the version height bumping for all projects in the repository. This is typically not desirable. Intuitively, a commit to `Bar` should only cause a version bump for `Bar`, and not `Foo` or `Quux`.

## Solution

Path filters provide a way to filter which subtrees in the repository affect version height. Imagine the `version.json` files had a `pathFilter` property:

```json
{
  "version": "1.0",
  "pathFilters": ["."]
}
```

With this single path filter of `"."`, the version height for this project would only bump when a commit was made within that subtree. Now imagine all projects in the original example have this value for `pathFilters`. Consider the following commits to the repository, and note their effect on the version height for each project:

| Paths changed                            | Result                                                                     |
| ---------------------------------------- | -------------------------------------------------------------------------- |
| `/README.md`                             | Commit does not affect any project. No versions change.                    |
| `/Bar/Program.cs`<br>`/Quux/Quux.csproj` | Commit affects both `Bar` and `Quux`. Their patch versions will bump by 1. |
| `/Bar/MyClass.cs`                        | Commit affects only `Bar`. `Bar`'s patch version will bump by 1.           |

When absent, the implied value for `pathFilters` is:

```json
{
  "pathFilters": [":/"]
}
```

This results in the entire repository tree being considered for version height calculations. This is the default behavior for GitVersioning.

## Path filter format

Path filters take on a variety of formats, and can specify paths relative to the `version.json` or relative to the root of the repository.

Multiple path filters may also be specified. The order is irrelevant. After a path matches any non-exclude path filter, it will be run through all exclude path filter. If it matches, the path is ignored.

| Path filter                                                                        | Description                                                                                                |
| ---------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------- |
| `./quux.txt`<br>`file-here.txt`<br>`sub-dir/foo.txt`<br>`../sibling/inclusion.txt` | File will be included. Path is relative to the `version.json` file.                                        |
| `./`<br>`sub-dir`<br>`../sibling`                                                  | Directory will be included. Path is relative to the `version.json` file.                                   |
| `/root-file.txt`<br>`:/dir/file.txt`                                               | File will be included. Path is absolute (i.e., relative to the root of the repository).                    |
| `:!bar.txt`<br>`:^../foo/baz.txt`                                                  | File will be excluded. Path is relative to the `version.json` file. `:!` and `:^` prefixes are synonymous. |
| `:!/root-file.txt`                                                                 | File will be excluded. Path is absolute (i.e., relative to the root of the repository).                    |

## Managing path filters with `nbgv path-filters`

For repositories with multiple projects and version.json files, manually maintaining `pathFilters` can be error-prone. The `nbgv path-filters` command automates this process by analyzing your MSBuild project structure and computing the correct path filters based on project references and shared build files.

### When to use path-filters command

Use the `nbgv path-filters` command in the following scenarios:

- **Monorepo with multiple projects** - You have multiple projects in different directories, each with their own `version.json`
- **Complex project dependencies** - Projects reference each other, and you need path filters to reflect these dependencies
- **Shared build files** - You use `Directory.Build.props` or other shared MSBuild imports that should be tracked by multiple projects
- **Maintaining accuracy** - You want to ensure path filters automatically stay in sync with your project structure

### How it works

The `nbgv path-filters` command uses the MSBuild project graph API to:

1. **Discover project files** - Finds all MSBuild project files (`.csproj`, `.vbproj`, etc.) associated with each `version.json`
2. **Compute transitive dependencies** - Uses the MSBuild project graph to determine the complete set of projects that each project depends on
3. **Include shared build files** - Identifies MSBuild imports like `Directory.Build.props` that reside within the repository
4. **Respect boundaries** - Stops searching for projects at directories containing their own `version.json` files, ensuring clean separation of concerns
5. **Generate path filters** - Converts the discovered projects and files into appropriate `pathFilters` entries

### Important behaviors

- **Only processes version.json files with projects** - A `version.json` file with no associated MSBuild projects is skipped and left unchanged
- **Respects version.json hierarchy** - When searching for projects under a `version.json`, the search stops at subdirectories that have their own `version.json` files
- **Includes project directories** - Path filters include entire project directories (e.g., `/ProjectA`) rather than individual `.csproj` files, making filters cleaner and more intuitive
- **Filters generated files** - Automatically excludes files in `obj/` and `bin/` directories (NuGet-generated imports)

### Usage

#### Check current path filters

To see what path filters should be present without making changes:

```ps1
nbgv path-filters check
```

This command will:
- Compare the computed path filters against what's currently in each `version.json`
- Display mismatches (missing or extra filters)
- Exit with non-zero code if any mismatches are found (useful for CI validation)

#### Update path filters

To automatically compute and update all `version.json` files:

```ps1
nbgv path-filters update
```

This command will:
- Compute the correct path filters for each `version.json`
- Update each file that needs changes
- Display which files were updated
- Skip any `version.json` files that have no associated projects

#### Specify which version.json files to process

By default, both commands search from the current directory. You can specify specific paths:

```ps1
nbgv path-filters check --path ./src/ProjectA --path ./src/ProjectB
```

#### Include additional project file extensions

By default, the tool searches for `.csproj` and `.vbproj` files. You can include other extensions:

```ps1
nbgv path-filters update --ext .fsproj --ext .csproj
```

### Example

Consider a monorepo with this structure:

```
/
  version.json (version: "1.0")
  Directory.Build.props
  /ProjectA
    version.json (version: "2.0")
    ProjectA.csproj
  /ProjectB
    version.json (version: "3.0")
    ProjectB.csproj
    (ProjectB.csproj references ProjectA.csproj)
```

Running `nbgv path-filters update` would produce:

**Root version.json** - Left unchanged (has no projects directly under it)

**ProjectA/version.json**:
```json
{
  "version": "2.0",
  "pathFilters": [
    "/ProjectA",
    "/Directory.Build.props"
  ]
}
```

**ProjectB/version.json**:
```json
{
  "version": "3.0",
  "pathFilters": [
    "/ProjectA",
    "/ProjectB",
    "/Directory.Build.props"
  ]
}
```

Note that ProjectB's filters include ProjectA because ProjectB depends on it. Any change to ProjectA's source files will now correctly trigger a version bump for ProjectB as well.

### CI Integration

You can use the `path-filters check` command in your CI pipeline to validate that `pathFilters` are correctly maintained:

```ps1
nbgv path-filters check
if ($LASTEXITCODE -ne 0) {
  Write-Error "Path filters are out of date. Run 'nbgv path-filters update' locally."
  exit 1
}
```

This ensures that developers keep path filters in sync with project structure changes.
