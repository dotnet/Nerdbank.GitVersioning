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

The recommended pattern is to create your NPM package from another directory
than your source directory so that the package.json can be version-stamped
without requiring a change to your source files.
In your checked-in version of package.json, set your `version` property to
`0.0.0-placeholder`:

```json
{
  "name": "your-package",
  "version": "0.0.0-placeholder",
}
```

Then write a gulp script that copies your files to package into another folder
and stamps the version into that folder.

```js
const outDir = 'out';

gulp.task('copyPackageContents', function() {
    return gulp
        .src([
            'package.json',
            'README.md',
            '*.js'
        ])
        .pipe(gulp.dest(outDir));
});

gulp.task('setPackageVersion', ['copyPackageContents'], function() {
    var nbgv = require(`./${outDir}`);
    // Stamp the copy of the NPM package in outDir, but use this
    // source directory as a reference for calculating the git version.
    return nbgv.setPackageVersion(outDir, '.');
});

gulp.task('package', ['setPackageVersion'], function() {
    return ap.execAsync(`npm pack "${path.join(__dirname, outDir)}"`, { cwd: outDir });
});

gulp.task('default', ['package'], function() {
});

``` 

When you run your gulp script, the out directory will contain a package
with a package.json file with a specific version field, such as:

```json
{
  "name": "nbgv-trial",
  "version": "1.0.15-g0b1ed99829",
}
```
