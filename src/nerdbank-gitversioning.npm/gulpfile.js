'use strict';
var gulp = require('gulp');
var ts = require('gulp-typescript');
var concat = require('gulp-concat');
var sourcemaps = require('gulp-sourcemaps');
var merge = require('merge2');
var tslint = require('gulp-tslint');
var del = require('del');

const outDir = 'out';
var tsProject = ts.createProject('tsconfig.json', { declarationFiles: true });

gulp.task('tsc', function() {
    var tsResult = gulp.src(['*.ts', 'ts/**/*.ts', 'typings/**/*.ts'])
        .pipe(tslint())
        .pipe(sourcemaps.init())
        .pipe(ts(tsProject));

    return merge([
        tsResult.dts.pipe(gulp.dest(`${outDir}/definitions`)),
        tsResult.js
            .pipe(sourcemaps.write(`maps`))
            .pipe(gulp.dest(outDir))
    ]);
});

gulp.task('copyPackageContents', ['tsc'], function() {
    return gulp
        .src([
            'package.json',
            '../../LICENSE.txt',
            '../../README.md'
        ])
        .pipe(gulp.dest(outDir));
});

gulp.task('setPackageVersion', ['copyPackageContents'], function() {
    var nbgv = require(`./${outDir}`);
    return nbgv.setPackageVersion(outDir, '.'); 
});

gulp.task('package', ['setPackageVersion'], function() {

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
