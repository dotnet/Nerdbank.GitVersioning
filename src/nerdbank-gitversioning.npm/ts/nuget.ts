import * as fs from 'fs';
import * as path from 'path';
import * as request from 'request';
import {existsAsync, mkdirIfNotExistAsync} from './asyncio';
import {execAsync} from './asyncprocess';

const nugetExePath = `${__dirname}/../tools/nuget.exe`;
const packagesFolder = `${__dirname}/../packages/`;

var downloadNuGetExePromise: Promise<string> = null;

async function downloadNuGetExeImpl(): Promise<string> {
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

function downloadNuGetExe(): Promise<string> {
    if (downloadNuGetExePromise) {
        return downloadNuGetExePromise;
    } else {
        return downloadNuGetExePromise = downloadNuGetExeImpl();
    }
}

var installNuGetPackagePromises = {};

export interface INuGetPackageInstallResult {
    packageDir: string;
    id: string;
    version: string;
}

export async function installNuGetPackage(packageId: string, version?: string): Promise<INuGetPackageInstallResult> {
    var nugetExePath = await downloadNuGetExe();

    if (!version) {
        var versionInfo = await execAsync(`${nugetExePath} list ${packageId}`);
        var regex = new RegExp(`${packageId} (\\S+)`);
        var matches = regex.exec(versionInfo.stdout);
        version = matches[1];
    }

    var packageLocation = path.join(packagesFolder, `${packageId}.${version}`);
    if (installNuGetPackagePromises[packageLocation]) {
        return installNuGetPackagePromises[packageLocation];
    } else {
        return installNuGetPackagePromises[packageLocation] = installNuGetPackageImpl(packageId, version);
    }
}

async function delay(millis) {
    await new Promise(resolve => setTimeout(resolve, millis));
}

async function installNuGetPackageImpl(packageId: string, version: string): Promise<INuGetPackageInstallResult> {
    var nugetExePath = await downloadNuGetExe();
    var packageLocation = path.join(packagesFolder, `${packageId}.${version}`);

    if (!(await existsAsync(packageLocation))) {
        console.log(`Installing ${packageId} ${version}...`);
        await mkdirIfNotExistAsync(packagesFolder);
        let cmdline = `${nugetExePath} install ${packageId} -OutputDirectory ${packagesFolder} -Version ${version} -Source https://ci.appveyor.com/nuget/nerdbank-gitversioning`;
        try {
            try {
                await execAsync(cmdline);
            } catch (err) {
                // try once more. Some bizarre race condition seems to kill us.
                await delay(250);
                await execAsync(cmdline);
            }
        } catch (err) {
            var e = new Error("Failed to install Nerdbank.GitVersioning nuget package");
            e['inner'] = err;
            throw e;
        }
    }

    return {
        packageDir: packageLocation,
        id: packageId,
        version: version
    };
};
