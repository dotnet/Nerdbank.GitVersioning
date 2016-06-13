'use strict';

import * as fs from 'fs';
import * as path from 'path';
import {installNuGetPackage} from './nuget'
import {execAsync} from './asyncprocess';

export async function getPackageVersion(projectDirectory: string) {
    var packageInstallPath = await installNuGetPackage('Nerdbank.GitVersioning', '1.4.41');
    var getVersionScriptPath = path.join(packageInstallPath.packageDir, "tools", "Get-Version.ps1");
    var semver1 = await execAsync(`powershell -ExecutionPolicy Bypass -Command (${getVersionScriptPath} -ProjectDirectory "${projectDirectory}").SemVer1`)
    return semver1.stdout;
}

getPackageVersion(process.env.npm_config_root || '.');
