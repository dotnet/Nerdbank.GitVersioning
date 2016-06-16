import * as cp from 'child_process';

export interface IExecAsyncResult {
    stdout: string;
    stderr: string;
}

export function execAsync(command: string, options?: cp.ExecOptions) {
    return new Promise<IExecAsyncResult>(
        (resolve, reject) => cp.exec(command, options, (error, stdout, stderr) => {
            if (error) {
                reject(error);
            } else {
                resolve({ stdout: stdout, stderr: stderr });
            }
        }));
};
