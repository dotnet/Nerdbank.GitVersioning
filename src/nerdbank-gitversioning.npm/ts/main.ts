'use string';

import * as lib from './index'

async function printGitVersion() {
    console.log(await lib.getGitVersion());
}

printGitVersion();
