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

Path filters take on a variety of formats, and can specify paths relative to the `version.json` or relative to the root of the repository. See the [Path filter format](#path-filter-format) section for more information.

Multiple path filters may also be specified. The order is irrelevant. After a path matches any non-exclude path filter, it will be run through all exclude path filter. If it matches, the path is ignored.

| Path filter                                                                        | Description                                                                                                |
| ---------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------- |
| `./quux.txt`<br>`file-here.txt`<br>`sub-dir/foo.txt`<br>`../sibling/inclusion.txt` | File will be included. Path is relative to the `version.json` file.                                        |
| `./`<br>`sub-dir`<br>`../sibling`                                                  | Directory will be included. Path is relative to the `version.json` file.                                   |
| `/root-file.txt`<br>`:/dir/file.txt`                                               | File will be included. Path is absolute (i.e., relative to the root of the repository).                    |
| `:!bar.txt`<br>`:^../foo/baz.txt`                                                  | File will be excluded. Path is relative to the `version.json` file. `:!` and `:^` prefixes are synonymous. |
| `:!/root-file.txt`                                                                 | File will be excluded. Path is absolute (i.e., relative to the root of the repository).                    |
