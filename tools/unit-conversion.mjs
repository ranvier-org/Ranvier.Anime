import { readFileSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import { createHash } from 'node:crypto';
const document = { body: null, addEventListener() {}, hidden: false };
globalThis.window = {};
globalThis.document = document;
globalThis.requestAnimationFrame = () => 0;
globalThis.cancelAnimationFrame = () => {};
globalThis.SVGElement = class {};
globalThis.NodeList = class {};
globalThis.HTMLCollection = class {};
Date.now = () => 0;
const { convertValueUnit } = await import('../upstream/anime/src/core/units.js');
const { animate } = await import('../upstream/anime/src/animation/index.js');
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const source = resolve(root, 'upstream/anime');
const commit = JSON.parse(readFileSync(resolve(root, 'upstream-provenance.json'), 'utf8')).commit;
const revision = execFileSync('rtk', ['proxy', 'git', '-C', source, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
const dirty = execFileSync('rtk', ['proxy', 'git', '-C', source, 'status', '--porcelain'], { encoding: 'utf8' }).trim();
if (revision !== commit || dirty) throw new Error('Expected clean pinned source');
const record = (input, force = true) => {
  const events = [];
  let width = '';
  const container = name => ({
    appendChild: () => events.push('append:' + name),
    removeChild: () => events.push('remove:' + name),
  });
  document.body = container('body');
  const parent = input.parent === 'document' ? document : input.parent === 'none' ? null : container('parent');
  const clone = {
    style: {
      set width(value) { width = value; events.push('width:' + value); },
    },
    get offsetWidth() {
      events.push('measure:' + width);
      return width === '100' + input.from ? input.widths[0] : input.widths[1];
    },
  };
  const target = { parentNode: parent, cloneNode: () => { events.push('clone'); return clone; } };
  const value = { t: 1, n: input.value, u: input.from, o: null, d: null, s: null };
  convertValueUnit(target, value, input.to, force);
  return { ...input, result: value.n, unit: value.u, events };
};
const inputs = [
  { value: 180, from: 'deg', to: 'rad', widths: [0, 0] },
  { value: Math.PI, from: 'rad', to: 'turn', widths: [0, 0] },
  { value: 0.25, from: 'turn', to: 'deg', widths: [0, 0] },
  { value: 5, from: 'px', to: 'px', widths: [100, 100] },
  { value: 2, from: 'em', to: 'px', widths: [1600, 100] },
  { value: 48, from: 'px', to: 'rem', widths: [100, 2400] },
  { value: 50, from: '%', to: 'px', widths: [400, 100] },
  { value: 50, from: 'px', to: '%', widths: [100, 400] },
  { value: -2.5, from: 'vw', to: 'px', widths: [1920, 100] },
  { value: 3.125, from: 'em', to: 'rem', widths: [1563, 2000] },
  { value: 2, from: 'em', to: 'px', widths: [0, 100] },
  { value: 2, from: 'em', to: 'px', widths: [150, 0] },
  { value: 2, from: 'em', to: 'px', widths: [0, 0] },
  { value: 1, from: 'cm', to: 'mm', widths: [3780, 378] },
  { value: 2, from: 'em', to: 'px', widths: [3200, 100], parent: 'document' },
  { value: 2, from: 'em', to: 'px', widths: [3200, 100], parent: 'none' },
];
const cases = inputs.map((input, index) => ({ name: 'unit' + index, ...record(input) }));
const cacheInputs = [
  { value: 17, from: 'em', to: 'px', widths: [1600, 100] },
  { value: 17, from: 'em', to: 'px', widths: [3200, 100] },
];
const cacheReplay = cacheInputs.map(input => record(input, false));
const animationInputs = [
  { name: 'em-to-px', base: '20px', first: '1em', last: '100px' },
  { name: 'relative-from-before-conversion', base: '20px', first: '+=1em', last: '100px' },
  { name: 'relative-target-before-conversion', base: '20px', first: '1em', last: '+=10px' },
  { name: 'px-to-percent', base: '0px', first: '40px', last: '50%' },
  { name: 'turn-to-deg', base: '0deg', first: '0.5turn', last: '270deg' },
  { name: 'unit-to-compound', base: '20px', first: '10px', last: 'calc(50% + 20px)' },
];
const animationCases = animationInputs.map(input => {
  const widths = { em: 1600, rem: 2400, px: 100, '%': 400, null: 0 };
  const create = initial => {
    let width = initial;
    const style = {
      get width() { return width; }, set width(value) { width = String(value); },
      getPropertyValue: () => width, setProperty: (_, value) => { width = String(value); },
    };
    return { nodeType: 1, style, parentNode: { appendChild() {}, removeChild() {} },
      cloneNode: () => create(width),
      get offsetWidth() { return widths[width.slice(3)] ?? 0; },
    };
  };
  const target = create(input.base);
  const animation = animate(target, { width: [input.first, input.last], duration: 100, ease: 'linear', composition: 'none', autoplay: false });
  if (animation._head._tweenType !== 2) throw new Error('Expected CSS unit conversion path');
  const samples = [0, 25, 50, 75, 100, 25].map(time => {
    animation.seek(time);
    return { time, value: target.style.width };
  });
  animation.cancel();
  return { ...input, samples };
});
const fixture = { upstreamCommit: commit, scope: 'recorded DOM measurements and source conversion, not a browser layout oracle', cases, cacheReplay, animationCases };
const output = JSON.stringify(fixture, null, 2) + '\n';
writeFileSync(resolve(root, 'tests/reference/unit-conversion.json'), output);
console.log('Captured ' + cases.length + ' unit conversions and the source global-cache context collision');
console.log('Fixture SHA256 ' + createHash('sha256').update(output).digest('hex'));
