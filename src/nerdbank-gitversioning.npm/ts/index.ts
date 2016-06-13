'use strict';

import http = require('http');
import * as q from 'q';
import * as request from 'request';
import * as fs from 'fs';
import {exec} from 'child_process';

function existsAsync(path: string) {
    return new Promise<boolean>(resolve => fs.exists(path, resolve));
};

function execAsync(command: string) {
    return new Promise<any>(
        (resolve, reject) => exec(command, (error, stdout, stderr) => {
            if (error) {
                reject(error);
            } else {
                resolve({ stdout: stdout, stderr: stderr });
            }
        }));
};

async function downloadNuGetExe(): Promise<string> {
    const nugetExePath = 'nuget.exe';

    if (!(await existsAsync(nugetExePath))) {
        console.log('Downloading nuget.exe...');
        var result = await new Promise<request.Request>(
            (resolve, reject) => {
                var req = request('https://dist.nuget.org/win-x86-commandline/latest/nuget.exe');
                req.pipe(fs.createWriteStream(nugetExePath))
                    .on('finish', () => resolve(req));
            });
    }

    return nugetExePath;
};

async function installNuGetPackage(packageId: string) {
    var nugetExePath = await downloadNuGetExe();
    console.log(`Installing ${packageId}...`);
    var result = await execAsync(`${nugetExePath} install ${packageId} -OutputDirectory .`);
    console.log(result.stdout);
};

export async function getPackageVersion(): Promise<string> {
    return "hi23";
}

installNuGetPackage('Nerdbank.GitVersioning');
