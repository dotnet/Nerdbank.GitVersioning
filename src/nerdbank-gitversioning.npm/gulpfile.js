'use strict';
var gulp = require('gulp');
var gutil = require('gulp-util');
var ts = require('gulp-typescript');
var sourcemaps = require('gulp-sourcemaps');
var merge = require('merge2');
// var tslint = require('gulp-tslint');
var del = require('del');
var path = require('path');

const outDir = 'out';
var tsProject = ts.createProject('tsconfig.json', { declarationFiles: true });

gulp.task('tsc', function() {
    var tsResult = gulp.src(['*.ts', 'ts/**/*.ts', 'typings/**/*.ts'])
        // .pipe(tslint())
        .pipe(sourcemaps.init())
        .pipe(ts(tsProject));

    return merge([
        tsResult.dts.pipe(gulp.dest(outDir)),
        tsResult.js
            .pipe(sourcemaps.write('.'))
            .pipe(gulp.dest(outDir))
    ]);
});

gulp.task('copyPackageContents', ['tsc'], function() {
    return gulp
        .src([
            'package.json',
            'README.md',
            '../../LICENSE.txt'
        ])
        .pipe(gulp.dest(outDir));
});

gulp.task('setPackageVersion', ['copyPackageContents'], function() {
    var nbgv = require(`./${outDir}`);
    return nbgv.setPackageVersion(outDir, '.');
});

gulp.task('package', ['setPackageVersion'], function() {
    var afs = require('./out/asyncio');
    var binDir =  '../../bin/js';
    return afs.mkdirIfNotExistAsync(binDir)
        .then(function() {
            var ap = require('./out/asyncprocess');
            return ap.execAsync(`npm pack "${path.join(__dirname, outDir)}"`, { cwd: binDir });
        });
});

gulp.task('clean', function() {
    return del([
        outDir
    ])
});

gulp.task('default', ['package'], function() {
});

gulp.task('watch', ['tsc'], function() {
    return gulp.watch('**/*.ts', ['tsc']);
});
