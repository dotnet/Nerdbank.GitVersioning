# Gulp support

```js
var gulp = require('gulp');
var nbgv = require('nerdbank-gitversioning')

gulp.task('default', function() {
    return nbgv.setPackageVersion();
});
```
