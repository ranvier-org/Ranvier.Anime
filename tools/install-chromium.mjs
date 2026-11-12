// Installs Playwright's Chromium for the npm package in the current directory.
//
// The system libraries come from apt only when Chromium does not start without them: a hosted runner already has
// them, and an apt mirror that does not resolve has failed whole CI runs.
import { spawnSync } from 'node:child_process';
import { createRequire } from 'node:module';
import path from 'node:path';
import { setTimeout as sleep } from 'node:timers/promises';

const require = createRequire(path.join(process.cwd(), 'package.json'));

function playwright(...args) {
  return spawnSync(`npx playwright ${args.join(' ')}`, { stdio: 'inherit', shell: true }).status === 0;
}

async function retried(attempts, step) {
  for (let attempt = 1; attempt <= attempts; attempt++) {
    if (step()) return true;
    if (attempt < attempts) {
      console.log(`Attempt ${attempt} of ${attempts} failed; retrying in ${attempt * 30} s.`);
      await sleep(attempt * 30_000);
    }
  }
  return false;
}

function chromium() {
  for (const name of ['@playwright/test', 'playwright', 'playwright-core']) {
    try {
      return require(name).chromium;
    } catch {
      // The package depends on one of the three.
    }
  }
  throw new Error(`No Playwright package in ${process.cwd()}`);
}

async function starts() {
  try {
    const browser = await chromium().launch();
    await browser.close();
    return true;
  } catch (error) {
    console.log(String(error?.message ?? error).split('\n')[0]);
    return false;
  }
}

if (!(await retried(3, () => playwright('install', 'chromium')))) process.exit(1);
if (await starts()) process.exit(0);
if (process.platform !== 'linux') process.exit(1);

console.log('Chromium does not start with the installed system libraries; installing them.');
if (!(await retried(4, () => playwright('install-deps', 'chromium')))) process.exit(1);
process.exit((await starts()) ? 0 : 1);
