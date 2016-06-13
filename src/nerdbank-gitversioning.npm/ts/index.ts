'use strict';

import * as q from 'q';
import request = require('request');
import fs = require('fs');

function downloadNuGetExe(): q.Promise<string> {
    console.log('Downloading nuget.exe...');
    request('https://dist.nuget.org/win-x86-commandline/latest/nuget.exe')
        .pipe(fs.createWriteStream('nuget.exe'));
    console.log("Done");
    return null;
}

function getPackageVersion(): string {
    return "hi23";
}

downloadNuGetExe();
