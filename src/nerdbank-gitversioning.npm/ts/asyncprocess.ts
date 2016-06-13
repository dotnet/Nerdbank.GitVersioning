import {exec} from 'child_process';

export function execAsync(command: string) {
    return new Promise<any>(
        (resolve, reject) => exec(command, (error, stdout, stderr) => {
            if (error) {
                reject(error);
            } else {
                resolve({ stdout: stdout, stderr: stderr });
            }
        }));
};
