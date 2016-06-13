import * as fs from 'fs';
import * as path from 'path';
import * as request from 'request';
import {existsAsync, mkdirIfNotExistAsync} from './asyncio';
import {execAsync} from './asyncprocess';

const nugetExePath = `${__dirname}/tools/nuget.exe`;
const packagesFolder = `${__dirname}/packages/`;

async function downloadNuGetExe(): Promise<string> {
    if (!(await existsAsync(nugetExePath))) {
        await mkdirIfNotExistAsync(path.dirname(nugetExePath));

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

export async function installNuGetPackage(packageId: string) {
    var nugetExePath = await downloadNuGetExe();
    await mkdirIfNotExistAsync(packagesFolder);
    console.log(`Installing ${packageId}...`);
    var result = await execAsync(`${nugetExePath} install ${packageId} -OutputDirectory .`);
    console.log(result.stdout);
    console.log(result.stderr);
};
