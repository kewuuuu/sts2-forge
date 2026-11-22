// Compatibility entry point. Both textures come from the same original painting.
const { spawnSync } = require('node:child_process');
const path = require('node:path');
const run = spawnSync(process.env.PYTHON || 'python', [path.join(__dirname, 'render-forge.py')], { stdio: 'inherit' });
if (run.error) console.error(run.error.message);
process.exitCode = run.status ?? 1;
