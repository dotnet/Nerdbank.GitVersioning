import {exec} from 'child_process';

export interface IExecAsyncResult {
    stdout: string;
    stderr: string;
}

export function execAsync(command: string) {
    return new Promise<IExecAsyncResult>(
        (resolve, reject) => exec(command, (error, stdout, stderr) => {
            if (error) {
                reject(error);
            } else {
                resolve({ stdout: stdout, stderr: stderr });
            }
        }));
};
