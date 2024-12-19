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
without requiring a change to your source files. If you have a many files to copy
or don't plan to commit changes, see [continuous integration](#continuous-integration) below.

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

const cp = require('child_process');
function execAsync(command, options) {
    return new Promise((resolve, reject) => cp.exec(command, options, (error, stdout, stderr) => {
        if (error) {
            reject(error);
        }
        else {
            resolve({ stdout: stdout, stderr: stderr });
        }
    }));
}

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
    return execAsync(`npm pack "${path.join(__dirname, outDir)}"`, { cwd: outDir });
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

## Continuous integration

If you do not plan to commit changes or have a lot of files you'd need to copy,
install of the instructions above you can instead ignore changes and simply build.
If you use package-lock.json or npm-shrinkwrap.json and use caching on your CI server,
you should rename those files prior to calling `nbgv.setPackageVersion()` as shown below.
If you do not rename the file, `nbgv.setPackageVersion()` may inadvertently modify the lockfile
and invalidate the cache, thus causing a cache miss on subsequent builds.

In your gulpfile.js:

```js
const fs = require('fs');
const util = require('util');

gulp.task('setPackageVersion', function() {
    const renameAsync = util.promisify(fs.rename);

    return renameAsync('package-lock.json', 'package-lock.backup').then(function() {
        return nbgv.setPackageVersion().finally(function() {
            return renameAsync('package-lock.backup', 'package-lock.json');
        });
    });
});
```

In your CI configuration, you can then safely use package-lock.json or npm-shrinkwrap.json
as part of the cache key, as shown in the Azure Pipelines example below:

```yaml
variables:
  npm_config_cache: $(Pipeline.Workspace)/.npm

steps:
- task: CacheBeta@0
  inputs:
    key: $(Agent.OS) | npm | package-lock.json
    path: $(npm_config_cache)

- script: npm ci
```
