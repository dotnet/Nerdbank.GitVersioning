'use strict';

import * as fs from 'fs';
import * as path from 'path';
var camelCase = require('camel-case')
import {installNuGetPackage} from './nuget'
import {execAsync} from './asyncprocess';

export async function getPackageVersion(projectDirectory: string) {
    var packageInstallPath = await installNuGetPackage('Nerdbank.GitVersioning', '1.4.41');
    var getVersionScriptPath = path.join(packageInstallPath.packageDir, "tools", "Get-Version.ps1");
    var versionText = await execAsync(`powershell -ExecutionPolicy Bypass -Command (${getVersionScriptPath} -ProjectDirectory "${projectDirectory}")`)
    var varsRegEx = /^(\w+)\s*: (.+)/mg;
    var match;
    var result = {};
    while (match = varsRegEx.exec(versionText.stdout)) {
        result[camelCase(match[1])] = match[2];
    }

    //console.log(result);
    return result;
}

getPackageVersion(process.env.npm_config_root || '.');
