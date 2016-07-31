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

## File format

The content of the version.json file is a JSON serialized object with these properties
(and more):

```js
{
  "version": "x.y-prerelease", // required
  "assemblyVersion": "x.y", // optional. Use when x.y for AssemblyVersionAttribute differs from the default version property.
  "buildNumberOffset": "zOffset", // optional. Use when you need to add/subtract a fixed value from the computed build number.
  "publicReleaseRefSpec": [
    "^refs/heads/master$", // we release out of master
    "^refs/tags/v\\d\\.\\d" // we also release tags starting with vN.N
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
  }
}
```

The `x` and `y` variables are for your use to specify a version that is meaningful
to your customers. Consider using [semantic versioning][semver] for guidance.

The optional -prerelease tag allows you to indicate that you are building prerelease software.

The `publicReleaseRefSpec` field causes builds out of certain branches or tags
to automatically drop the `-gabc123` git commit ID suffix from the version, making it
convenient to build releases out of these refs with a friendly version number
that assumes linear versioning.
