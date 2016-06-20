'use string';

import * as lib from './index'

async function printGitVersion() {
    try {
        console.log(await lib.getVersion());
    } catch (err) {
        console.log('Failed to get version:');
        console.log(err);
    }
}

printGitVersion();
