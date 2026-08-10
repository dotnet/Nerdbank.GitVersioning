import * as fs from 'fs';
import * as path from 'path';
import { execSync } from 'child_process';

const nbgvPath = 'nbgv.cli';
const preferredFrameworks = ["net10.0", "net9.0", "net8.0"];

function getInstalledRuntimeMajors(dotnetCommand: string): Set<number> {
    try {
        const output = execSync(`${dotnetCommand} --list-runtimes`, { encoding: 'utf8' });
        const majors = new Set<number>();
        for (const line of output.split(/\r?\n/)) {
            const match = line.match(/^Microsoft\.NETCore\.App\s+(\d+)\./);
            if (match) {
                majors.add(Number(match[1]));
            }
        }

        return majors;
    } catch {
        return new Set<number>();
    }
}

function getFrameworkMajor(targetFramework: string): number | undefined {
    const match = targetFramework.match(/^net(\d+)\.0$/);
    return match ? Number(match[1]) : undefined;
}

export function getNbgvCommand(dotnetCommand?: string): string {
    var command = dotnetCommand || 'dotnet';
    const installedRuntimes = getInstalledRuntimeMajors(command);

    for (const targetFramework of preferredFrameworks) {
        const nbgvDll = path.join(__dirname, nbgvPath, "tools", targetFramework, "any", "nbgv.dll");
        const major = getFrameworkMajor(targetFramework);
        if (fs.existsSync(nbgvDll) && major !== undefined && installedRuntimes.has(major)) {
            return `${command} "${nbgvDll}"`;
        }
    }

    for (const targetFramework of preferredFrameworks) {
        const nbgvDll = path.join(__dirname, nbgvPath, "tools", targetFramework, "any", "nbgv.dll");
        if (fs.existsSync(nbgvDll)) {
            return `${command} "${nbgvDll}"`;
        }
    }

    throw new Error(`Could not find nbgv tool under any of: ${preferredFrameworks.join(', ')}`);
}
