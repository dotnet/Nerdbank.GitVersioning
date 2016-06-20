'use strict';

import * as fs from 'fs';
import * as path from 'path';
var camelCase = require('camel-case')
import {execAsync} from './asyncprocess';

const nbgvPath = 'nbgv.nuget';

/**
 * The various aspects of a version that can be calculated.
 */
export interface IGitVersion {
    cloudBuildNumber: string,
    cloudBuildNumberEnabled: boolean,
    buildMetadataWithCommitId: string,
    assemblyVersion: string,
    assemblyFileVersion: string,
    assemblyInformationalVersion: string,
    publicRelease: boolean,
    prereleaseVersion: string,
    simpleVersion: string,
    buildNumber: string,
    majorMinorVersion: string,
    gitCommitId: string,
    gitCommitIdShort: string,
    versionHeight: string,
    version: string,
    cloudBuildVersionVarsEnabled: boolean,
    cloudBuildVersionVars: string,
    buildMetadata: string,
    buildMetadataFragment: string,
    nuGetPackageVersion: string,
    npmPackageVersion: string,
    semVer1: string,
    semVer2: string
}

/**
 * Gets an object describing various aspects of the version of a project.
 * @param projectDirectory The directory of the source code to get the version of.
 */
export async function getVersion(projectDirectory?: string): Promise<IGitVersion> {
    projectDirectory = projectDirectory || '.';
    var getVersionScriptPath = path.join(__dirname, nbgvPath, "tools", "Get-Version.ps1");
    var versionText = await execAsync(`powershell -ExecutionPolicy Bypass -Command (${getVersionScriptPath} -ProjectDirectory "${projectDirectory}")`)
    if (versionText.stderr) {
        throw versionText.stderr;
    }

    var varsRegEx = /^(\w+)\s*: (.+)/mg;
    var match;
    var result = {};
    while (match = varsRegEx.exec(versionText.stdout)) {
        // Do a few type casts if appropriate.
        let value = match[2];
        if (value.toUpperCase() === 'TRUE') {
            value = true;
        } else if (value.toUpperCase() === 'FALSE') {
            value = false;
        }

        result[camelCase(match[1])] = value;
    }

    return <IGitVersion>result;
}

/**
 * Sets an NPM package version based on the git height and version.json.
 * @param packageDirectory The directory of the package about to be published.
 * @param srcDirectory The directory of the source code behind the package, if different. 
 */
export async function setPackageVersion(packageDirectory?: string, srcDirectory?: string) {
    packageDirectory = packageDirectory || '.';
    srcDirectory = srcDirectory || packageDirectory;
    const gitVersion = await getVersion(srcDirectory);
    console.log(`Setting package version to ${gitVersion.semVer1}`);
    var result = await execAsync(`npm version ${gitVersion.semVer1} --no-git-tag-version`, { cwd: packageDirectory });
    if (result.stderr) {
        console.log(result.stderr);
    }
}

/**
 * Sets the package version to 0.0.0-placeholder, so as to obviously indicate
 * that the version isn't set in the source code version of package.json.
 * @param srcDirectory The directory of the source code behind the package, if different. 
 */
export async function resetPackageVersionPlaceholder(srcDirectory?: string) {
    srcDirectory = srcDirectory || '.';
    var result = await execAsync(`npm version 0.0.0-placeholder`, { cwd: srcDirectory });
    if (result.stderr) {
        console.log(result.stderr);
    }
}
