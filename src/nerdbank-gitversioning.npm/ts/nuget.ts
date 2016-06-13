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

export async function installNuGetPackage(packageId: string, version?: string) {
    var nugetExePath = await downloadNuGetExe();

    if (!version) {
        var versionInfo = await execAsync(`${nugetExePath} list ${packageId}`);
        var regex = new RegExp(`${packageId} (\\S+)`);
        var matches = regex.exec(versionInfo.stdout);
        version = matches[1];
    }

    var packageLocation = path.join(packagesFolder, `${packageId}.${version}`);

    if (!(await existsAsync(packageLocation))) {
        console.log(`Installing ${packageId} ${version}...`);
        await mkdirIfNotExistAsync(packagesFolder);
        var result = await execAsync(`${nugetExePath} install ${packageId} -OutputDirectory ${packagesFolder} -Version ${version}`);
    }

    return {
        packageDir: packageLocation,
        id: packageId,
        version: version
    };
};
