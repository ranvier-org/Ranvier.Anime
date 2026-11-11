import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { rolldown } from 'rolldown';
import { chromium } from 'playwright';

const here = path.dirname(fileURLToPath(import.meta.url));
const bundle = await rolldown({ input: path.join(here, 'generated/CssChecks.js'), logLevel: 'silent' });
const { output: [chunk] } = await bundle.generate({ format: 'iife', name: 'CssBridge', exports: 'named' });
await bundle.close();
let executablePath = process.env.CSS_CHROMIUM_EXECUTABLE;
if (!executablePath) {
  try { await fs.access(chromium.executablePath()); }
  catch {
    const cache = process.env.LOCALAPPDATA ? path.join(process.env.LOCALAPPDATA, 'ms-playwright') : null;
    if (cache) {
      const versions = (await fs.readdir(cache)).filter(name => /^chromium-\d+$/.test(name)).sort((a, b) => Number(b.split('-')[1]) - Number(a.split('-')[1]));
      for (const version of versions) {
        const candidate = path.join(cache, version, 'chrome-win64/chrome.exe');
        try { await fs.access(candidate); executablePath = candidate; break; } catch {}
      }
    }
  }
}
const browser = await chromium.launch({ headless: true, ...(executablePath ? { executablePath } : {}) });
try {
  const page = await browser.newPage();
  await page.setContent('<!doctype html><html><head></head><body></body></html>');
  await page.addScriptTag({ content: chunk.code });
  await page.evaluate(() => globalThis.CssBridge.browserExpressions());
  console.log('Chromium: typed custom properties drive CSS expression widths and pseudo-elements at start, midpoint and completion.');
} finally { await browser.close(); }
