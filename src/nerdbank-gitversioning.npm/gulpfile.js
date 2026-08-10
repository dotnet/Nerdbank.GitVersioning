'use strict';
var gulp = require('gulp');
// var tslint = require('gulp-tslint');
var path = require('path');
var cp = require('child_process');
var util = require('util');

const outDir = 'out';
var execFileAsync = util.promisify(cp.execFile);
var typeScriptCompilerPath = require.resolve('typescript');

gulp.task('tsc', async function () {
    await execFileAsync(process.execPath, [path.join(path.dirname(typeScriptCompilerPath), '..', 'bin', 'tsc')]);
});

gulp.task('copyPackageContents', gulp.series('tsc', function () {
    return gulp
        .src([
            'package.json',
            'README.md',
            '../../LICENSE'
        ])
        .pipe(gulp.dest(outDir));
}));

gulp.task('setPackageVersion', gulp.series('copyPackageContents', function () {
    var nbgv = require(`./${outDir}`);
    return nbgv.setPackageVersion(outDir, '.');
}));

gulp.task('package', gulp.series('setPackageVersion', function () {
    var afs = require('./out/asyncio');
    var binDir = '../../bin/js';
    return afs.mkdirIfNotExistAsync(binDir)
        .then(function () {
            var ap = require('./out/asyncprocess');
            return ap.execAsync(`npm pack "${path.join(__dirname, outDir)}"`, { cwd: binDir });
        });
}));

gulp.task('clean', async function () {
    const del = await import('del');
    await del.deleteAsync([outDir]);
});

gulp.task('default', gulp.series('package', function (done) {
    done();
}));

gulp.task('watch', gulp.series('tsc', function () {
    return gulp.watch('**/*.ts', gulp.series('tsc'));
}));

gulp.task('test', gulp.series('tsc', async function () {
    var nbgv = require('./out');
    var v = await nbgv.getVersion();
    console.log(v);
}));
