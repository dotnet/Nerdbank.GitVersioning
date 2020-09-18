# nerdbank-gitversioning

With this package, and a version.json file to express your version number
checked into the root of your git repo:

```json
{
  "version": "1.0-beta"
}
```

Your NPM packages and other builds can be automatically stamped with a
version that precisely describes the git commit that built it.

## CLI use

Stamp your package.json file with the git-based version:

```sh
nbgv-setversion
```

Reset your package.json file with a version placeholder (suitable for checking in):

```sh
nbgv-setversion --reset
```

Or invoke the `nbgv` tool directly for many options:

```sh
nbgv -?
```

### Pack script

A possible script to pack your NPM package:

```sh
yarn nbgv-setversion
yarn pack
yarn nbgv-setversion --reset
```

## Programmatic consumption

```ts
import * as nbgv from 'nerdbank-gitversioning'

// Retrieve all sorts of version information. Print just one bit.
const versionInfo = await nbgv.getVersion();
console.log(versionInfo.npmPackageVersion);

// Stamp the package.json file in the current directory with the computed version.
await nbgv.setPackageVersion();

// After packing, reset your package.json file to using a placeholder version number.
await nbgv.resetPackageVersionPlaceholder();
```

See our [project README][GitHubREADME] for more information.

[GitHubREADME]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/master/README.md
