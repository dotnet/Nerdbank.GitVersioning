# Node.js

First, you should [acquire the `nerdbank-gitversioning` NPM package](../npm-acquisition.md).

## Acquiring version information

```js
var nbgv = require('nerdbank-gitversioning')
nbgv.getVersion()
    .then(r => console.log(r))
    .catch(e => console.error(e));
```

Will print out a JavaScript object resembling this:

```json
{ "version": "0.0.1.24231",
  "simpleVersion": "0.0.1",
  "majorMinorVersion": "0.0",
  "commitId": "a75ed9bf5388d6a6c89ea7377b2bc0217523c12d",
  "commitIdShort": "a75ed9bf53",
  "versionHeight": "1",
  "semVer1": "0.0.1-ga75ed9bf53",
  "semVer2": "0.0.1+ga75ed9bf53" }
```

## Build integration

Check out our [instructions for gulp](../build-systems/gulp.md).
