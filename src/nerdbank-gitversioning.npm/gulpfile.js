'use strict';
var gulp = require('gulp');
var ts = require('gulp-typescript');
var concat = require('gulp-concat');
var sourcemaps = require('gulp-sourcemaps');
var merge = require('merge2');

var tsProject = ts.createProject('tsconfig.json', { declarationFiles: true });

gulp.task('tsc', function() {
    var tsResult = gulp.src(['*.ts', 'ts/**/*.ts', 'typings/**/*.ts'])
        .pipe(sourcemaps.init())
        .pipe(ts(tsProject));

    return merge([
        tsResult.dts.pipe(gulp.dest('release/definitions')),
        tsResult.js
            .pipe(concat('index.js'))
            .pipe(sourcemaps.write())
            .pipe(gulp.dest('release/js'))
    ]);
});

gulp.task('default', ['tsc'], function() {
});

gulp.task('watch', ['tsc'], function() {
    gulp.watch('**/*.ts', ['tsc']);
});
