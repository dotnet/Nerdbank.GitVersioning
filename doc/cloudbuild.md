# Cloud build support

Nerdbank.GitVersioning implicitly supports all cloud build services and CI
server software because it simply uses git itself and integrates naturally
in MSBuild, gulp and other build scripts.

## Requirements

1. Your CI build should be configured to actually clone the git repo rather than
   download sources (i.e. the '.git' folder is required).
1. Do not enable any 'shallow clone' option on your CI build, as that erases
   git history that is required for accurate version calculation.

## Optional features

By specifying certain `cloudBuild` options in your `version.json` file,
you can activate features for some cloud build systems, as follows

### Automatically match cloud build numbers to to your git version

Cloud builds tend to associate some calendar date or monotonically increasing
build number to each build. These build numbers are not very informative, if at all.
Instead, Nerdbank.GitVersioning can automatically set your cloud build's
build number to equal the semver version calculated during your build. 

Enable this feature by setting the `cloudBuild.buildNumber.enabled` field
in your `version.json` file to `true`, as shown below:

```json
{
  "version": "1.0",
  "cloudBuild": {
    "buildNumber": {
      "enabled": true
    }
  }
}
```

### Set special build variables for use in subsequent build steps.

| Build variable | MSBuild property | Sample value
| --- | --- | --- |
| GitAssemblyInformationalVersion | AssemblyInformationalVersion | 1.3.1+g15e1898f47
| GitBuildVersion | BuildVersion | 1.3.1.57621

This means you can use these variables in subsequent steps in your cloud build
such as publishing artifacts, so that your richer version information can be
expressed in the publish location or artifact name.

Enable this feature by setting the `cloudBuild.setVersionVariables` field
in your `version.json` file to `true`, as shown below:

```json
{
  "version": "1.0",
  "cloudBuild": {
    "setVersionVariables": true
  }
}
```

[Issue37]: https://github.com/AArnott/Nerdbank.GitVersioning/issues/37
