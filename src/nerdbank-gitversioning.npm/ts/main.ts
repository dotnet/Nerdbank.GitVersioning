#!/usr/bin/env node

'use strict';

import { getNbgvCommand } from "./core";

const { spawn } = require('child_process');
const { argv, exit } = require('process');

const cp = spawn(`${getNbgvCommand()} ${argv.slice(2).join(' ')}`, { shell: true, stdio: "inherit" })
cp.once('exit', code => exit(code))
