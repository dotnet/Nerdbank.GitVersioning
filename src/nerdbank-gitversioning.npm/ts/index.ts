'use strict';

import * as fs from 'fs';
import * as path from 'path';
var camelCase = require('camel-case')
import {execAsync} from './asyncprocess';

const nbgvPath = 'nbgv.cli';

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
    var getVersionScriptPath = path.join(__dirname, nbgvPath, "tools", "netcoreapp3.0", "any", "nbgv.dll");
    var versionText = await execAsync(`dotnet "${getVersionScriptPath}" get-version --format json`, { cwd: projectDirectory })
    if (versionText.stderr) {
        throw versionText.stderr;
    }

    var directResult = JSON.parse(versionText.stdout);
    var result = {};
    for (var field in directResult) {
        result[camelCase(field)] = directResult[field];
    }

    return <IGitVersion>result;
}

/**
 * Sets an NPM package version based on the git height and version.json.
 * @param packageDirectory The directory of the package about to be published.
 * @param srcDirectory The directory of the source code behind the package, if different than the packageDirectory.
 */
export async function setPackageVersion(packageDirectory?: string, srcDirectory?: string) {
    packageDirectory = packageDirectory || '.';
    srcDirectory = srcDirectory || packageDirectory;
    const gitVersion = await getVersion(srcDirectory);
    console.log(`Setting package version to ${gitVersion.npmPackageVersion}`);
    var result = await execAsync(`npm version ${gitVersion.npmPackageVersion} --no-git-tag-version`, { cwd: packageDirectory });
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
