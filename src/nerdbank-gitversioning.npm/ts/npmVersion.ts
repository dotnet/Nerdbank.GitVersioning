#!/usr/bin/env node

import * as lib from './index.js'

(async () => {
    if (process.argv[2] === '--reset') {
        await lib.resetPackageVersionPlaceholder();
    } else {
        await lib.setPackageVersion();
    }
})();
