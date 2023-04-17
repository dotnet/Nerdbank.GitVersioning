import * as fs from 'fs';
import * as path from 'path';

const nbgvPath = 'nbgv.cli';

export function getNbgvCommand(dotnetCommand?: string): string {
    var command = dotnetCommand || 'dotnet';
    const nbgvDll = path.join(__dirname, nbgvPath, "tools", "net6.0", "any", "nbgv.dll");
    return `${command} "${nbgvDll}"`;
}
