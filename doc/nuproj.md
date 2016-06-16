# NuProj support

When building NuGet packages with NuProj, you can install the Nerdbank.GitVersioning
NuGet package into your NuProj project itself to automatically start versioning your
own packages to match the git versioning rules specified in your `version.json` file.

## Installing Nerdbank.GitVersioning into your NuProj project

First, you should make sure that your NuProj follows [these build authoring
guidelines](https://github.com/nuproj/nuproj/blob/master/docs/Build.md).

Add Nerdbank.GitVersioning as a package to your NuProj's `project.json` file.
It may end up looking something like this:

```json
{
  "dependencies": {
    "Nerdbank.GitVersioning": "1.4.41",
    "NuProj": "0.10.48-beta-gea4a31bbc5"
  },
  "frameworks": {
    "net451": { }
  },
  "runtimes": {
    "win": { }
  }
}
```

## Additional steps

You are encouraged to *remove* any definition of a Version property,
since it will be set by the build.

```xml
<Version>1.0.0-removeThisWholeLine</Version>
```
