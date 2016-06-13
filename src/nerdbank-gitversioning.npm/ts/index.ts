'use strict';

import {installNuGetPackage} from './nuget'

export async function getPackageVersion(): Promise<string> {
    return "hi23";
}

installNuGetPackage('Nerdbank.GitVersioning');
