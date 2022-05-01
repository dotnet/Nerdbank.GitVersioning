'use string';

import * as fs from 'fs';

export function existsAsync(path: string) {
    return new Promise<boolean>(resolve => fs.exists(path, resolve));
};

export function mkdirAsync(path: string) {
    return new Promise<any>((resolve, reject) => fs.mkdir(path, err => {
        if (err) { reject(err); } else { resolve(null) }
    }));
}

export async function mkdirIfNotExistAsync(path: string) {
    if (!(await existsAsync(path))) {
        await mkdirAsync(path);
    }
}
