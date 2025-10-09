# version.json file

You must define a version.json file in your project directory or some ancestor of it.
It is common to define it in the root of your git repo.

**Important**: Some changes to version.json are not effective until you *commit* the change.
Pushing your commit to a remote is not necessary.

Here is the content of a sample version.json file you may start with:

```json
{
  "$schema": "https://raw.githubusercontent.com/dotnet/Nerdbank.GitVersioning/main/src/NerdBank.GitVersioning/version.schema.json",
  "version": "1.0-beta"
}
```

The `$schema` field is optional but highly encouraged as it causes most JSON editors
to add auto-completion and doc tips to help you author the file.

Note that the capitalization of the `version.json` filename must be all lower-case
when added to the git repo.

## File format

The content of the version.json file is a JSON serialized object with these properties
(and more):

```js
{
  "version": "x.y-prerelease", // required (unless the "inherit" field is set to true and a parent version.json file sets this.)
  "assemblyVersion": {
    "version": "x.y", // optional. Use when x.y for AssemblyVersionAttribute differs from the default version property.
    "precision": "revision" // optional. Use when you want a more precise assembly version than the default major.minor.
  },
  "versionHeightOffset": "zOffset", // optional. Use when you need to add/subtract a fixed value from the computed version height.
  "versionHeightOffsetAppliesTo": "x.y-prerelease", // optional. Specifies the version to which versionHeightOffset applies. When the version changes such that version height would reset, and this doesn't match the new version, versionHeightOffset is ignored.
  "semVer1NumericIdentifierPadding": 4, // optional. Use when your -prerelease includes numeric identifiers and need semver1 support.
  "gitCommitIdShortFixedLength": 10, // optional. Set the commit ID abbreviation length.
  "gitCommitIdShortAutoMinimum": 0, // optional. Set to use the short commit ID abbreviation provided by the git repository.
  "nugetPackageVersion": {
     "semVer": 1 // optional. Set to either 1 or 2 to control how the NuGet package version string is generated. Default is 1.
     "precision": "build" // optional. Use when you want to use a more or less precise package version than the default major.minor.build.
  },
  "pathFilters": [
    // optional list of paths to consider when calculating version height.
  ],
  "publicReleaseRefSpec": [
    "^refs/heads/master$", // we release out of master
    "^refs/tags/v\\d+\\.\\d+" // we also release tags starting with vN.N
  ],
  "cloudBuild": {
    "setVersionVariables": true,
    "buildNumber": {
      "enabled": false,
      "includeCommitId": {
        "when": "nonPublicReleaseOnly",
        "where": "buildMetadata"
      }
    }
  },
  "release" : {
    "tagName" : "v{version}",
    "branchName" : "v{version}",
    "versionIncrement" : "minor",
    "firstUnstableTag" : "alpha"
  },
  "inherit": false // optional. Set to true in secondary version.json files used to tweak settings for subsets of projects.
}
```

The `x` and `y` variables are for your use to specify a version that is meaningful
to your customers. Consider using [semantic versioning](https://semver.org/) for guidance.
You may optionally supply a third integer in the version (i.e. x.y.z),
in which case the git version height is specified as the fourth integer,
which only appears in certain version representations.
Alternatively, you can include the git version height in the -prerelease tag using
syntax such as: `1.2.3-beta.{height}`

The optional -prerelease tag allows you to indicate that you are building prerelease software.

The `publicReleaseRefSpec` field causes builds out of certain branches or tags
to automatically drop the `-gabc123` git commit ID suffix from the version, making it
convenient to build releases out of these refs with a friendly version number
that assumes linear versioning.

When the `cloudBuild.buildNumber.includeCommitId.where` property is set to `fourthVersionComponent`, the first 15 bits of the commit hash is used to create the 4th integer in the version number.

## Version Height Offset

The `versionHeightOffset` property allows you to add or subtract a fixed value from the git version height. This is typically used as a temporary workaround when migrating from another versioning system or when correcting version numbering discrepancies.

The `versionHeightOffsetAppliesTo` property can be used in conjunction with `versionHeightOffset` to ensure that the offset is only applied when the version matches a specific value. When the `version` property changes such that the version height would be reset, and `versionHeightOffsetAppliesTo` does not match the new version, the `versionHeightOffset` will be automatically ignored.

This allows version height offsets to implicitly reset as intended when the version changes, without having to manually remove the offset properties from all `version.json` files in the repository.

### Example

```json
{
  "version": "1.0-beta",
  "versionHeightOffset": 100,
  "versionHeightOffsetAppliesTo": "1.0-beta"
}
```

In this example, the offset of 100 will be applied as long as the version remains "1.0-beta". When you update the version to "1.1-alpha" (which would reset the version height), the offset will be automatically ignored because "1.1-alpha" does not match "1.0-beta".

> [!NOTE]
> This feature is particularly useful when a `version.json` file uses `"inherit": true` to get the version from a parent `version.json` file higher in the source tree. In such cases, you can set `versionHeightOffset` and `versionHeightOffsetAppliesTo` in the inheriting file without having to update it when the parent version changes. The offset will automatically stop applying when the inherited version no longer matches `versionHeightOffsetAppliesTo`.

[Learn more about pathFilters](path-filters.md).
