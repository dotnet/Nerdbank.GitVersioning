# version.json file

You must define a version.json file in your project directory or some ancestor of it.
It is common to define it in the root of your git repo.
Here is the content of a sample version.json file you may start with:

```json
{
  "$schema": "https://raw.githubusercontent.com/AArnott/Nerdbank.GitVersioning/master/src/NerdBank.GitVersioning/version.schema.json",
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
  "assemblyVersion": "x.y", // optional. Use when x.y for AssemblyVersionAttribute differs from the default version property.
  "buildNumberOffset": "zOffset", // optional. Use when you need to add/subtract a fixed value from the computed build number.
  "semVer1NumericIdentifierPadding": 4, // optional. Use when your -prerelease includes numeric identifiers and need semver1 support.
  "nugetPackageVersion": {
     "semVer": 1 // optional. Set to either 1 or 2 to control how the NuGet package version string is generated. Default is 1.
  },
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
  "inherit": false // optional. Set to true in secondary version.json files used to tweak settings for subsets of projects.
}
```

The `x` and `y` variables are for your use to specify a version that is meaningful
to your customers. Consider using [semantic versioning][semver] for guidance.
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
