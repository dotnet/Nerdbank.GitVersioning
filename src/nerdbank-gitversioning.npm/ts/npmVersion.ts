#!/usr/bin/env node

import * as lib from './index'

(async () => {
    if (process.argv[2] === '--reset') {
        await lib.resetPackageVersionPlaceholder();
    } else {
        await lib.setPackageVersion();
    }
})();
