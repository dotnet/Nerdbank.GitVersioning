# VSIX projects

Nerdbank.GitVersioning can automatically stamp the VSIXs you build with
the version calculated from your version.json file and git height.

## Installation

1. [Install the Nerdbank.GitVersioning NuGet package](../nuget-acquisition.md) into your VSIX-generating project.
1. Open the `source.extension.vsixmanifest` file in a code editor
and set the `PackageManifest/Metadata/Identity/@Version` attribute to this
value: `|%CurrentProject%;GetBuildVersion|`
