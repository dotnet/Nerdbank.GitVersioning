'use strict';

import http = require('http');
import * as q from 'q';
import * as request from 'request';
import * as fs from 'fs';

async function existsAsync(path: string) {
    return new Promise<boolean>(resolve => fs.exists(path, resolve));
};

export async function downloadNuGetExe(): Promise<string> {
    const nugetExePath = 'nuget.exe';

    if (!(await existsAsync(nugetExePath))) {
        console.log('Downloading nuget.exe...');
        var result = await new Promise<request.Request>(
            (resolve, reject) => {
                var req = request('https://dist.nuget.org/win-x86-commandline/latest/nuget.exe');
                req.pipe(fs.createWriteStream(nugetExePath))
                    .on('finish', () => resolve(req));
            });
        console.log('Download successful.');
    }

    return nugetExePath;
};

function getPackageVersion(): string {
    return "hi23";
}

downloadNuGetExe();
