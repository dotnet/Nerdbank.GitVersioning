# Gulp support

You can invoke Nerdbank.GitVersioning from a gulp task to get
version information and even to automatically stamp your NPM packages.

The following gulp script will update your package.json file's version
property with the package version to build.  

```js
var gulp = require('gulp');
var nbgv = require('nerdbank-gitversioning')

gulp.task('default', function() {
    return nbgv.setPackageVersion();
});
```
