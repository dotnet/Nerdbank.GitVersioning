'use strict';

import { execAsync } from './asyncprocess';
import { getNbgvCommand } from './core';

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
 * @param dotnetCommand The location of the dotnet command line executable
 */
export async function getVersion(projectDirectory?: string, dotnetCommand?: string): Promise<IGitVersion> {
    projectDirectory = projectDirectory || '.';
    var versionText = await execAsync(`${getNbgvCommand(dotnetCommand)} get-version --format json`, { cwd: projectDirectory })
    if (versionText.stderr) {
        throw versionText.stderr;
    }

    var directResult = JSON.parse(versionText.stdout);
    var result = {};
    for (var field in directResult) {
        const camelCaseFieldName = field.charAt(0).toLowerCase() + field.slice(1);
        result[camelCaseFieldName] = directResult[field];
    }

    return <IGitVersion>result;
}

/**
 * Sets an NPM package version based on the git height and version.json.
 * @param packageDirectory The directory of the package about to be published.
 * @param srcDirectory The directory of the source code behind the package, if different than the packageDirectory.
 * @param dotnetCommand The location of the dotnet command line executable
 */
export async function setPackageVersion(packageDirectory?: string, srcDirectory?: string, dotnetCommand?: string) {
    packageDirectory = packageDirectory || '.';
    srcDirectory = srcDirectory || packageDirectory;
    const gitVersion = await getVersion(srcDirectory, dotnetCommand);
    console.log(`Setting package version to ${gitVersion.npmPackageVersion}`);
    var result = await execAsync(`npm version ${gitVersion.npmPackageVersion} --no-git-tag-version --allow-same-version`, { cwd: packageDirectory });
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
    var result = await execAsync(`npm version 0.0.0-placeholder --no-git-tag-version --allow-same-version`, { cwd: srcDirectory });
    if (result.stderr) {
        console.log(result.stderr);
    }
}
