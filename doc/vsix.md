# VSIX support

Nerdbank.GitVersioning can automatically stamp the VSIXs you build with
the version calculated from your version.json file and git height. 

## Installation

1. Install the Nerdbank.GitVersioning NuGet package into your VSIX-generating project.
1. Open the `source.extension.vsixmanifest` file in a code editor
and set the `PackageManifest/Metadata/Identity/@Version` attribute to this
value: `|YourProjectName;GetBuildVersion|` where `YourProjectName` is
obviously replaced with the actual project name (without extension) of your
VSIX project.
