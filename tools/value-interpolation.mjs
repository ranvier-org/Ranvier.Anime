import { readFileSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import { createHash } from 'node:crypto';
Date.now = () => 0;
const { animate } = await import('../upstream/anime/src/animation/index.js');
const { decomposeRawValue, createDecomposedValueTargetObject } = await import('../upstream/anime/src/core/values.js');
const { globals } = await import('../upstream/anime/src/core/globals.js');
const { round } = await import('../upstream/anime/src/core/helpers.js');
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const source = resolve(root, 'upstream/anime');
const commit = JSON.parse(readFileSync(resolve(root, 'upstream-provenance.json'), 'utf8')).commit;
const revision = execFileSync('rtk', ['proxy', 'git', '-C', source, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
const dirty = execFileSync('rtk', ['proxy', 'git', '-C', source, 'status', '--porcelain'], { encoding: 'utf8' }).trim();
if (revision !== commit || dirty) throw new Error('Expected clean pinned source');
const inputs = [
  [0, 0, 100], [20, '+=5', '*=3'], [20, '-=5', '-=30'], [0, '1.23456789', '9.87654321'],
  [0, '1px', '9.87654321px'], [0, '20%', '80%'], [0, 0, '100px'], [0, '10px', 50],
  [0, '1e-7px', '1e+3px'], [0, '0deg', '360deg'], [0, '0px', '+=20px'],
  [0, '#000', '#fff'], [0, '#1234', '#abcdef80'], [0, '#ff0000', '#0000ff'],
  [0, 'rgb(10,20,30)', 'rgba(100,200,250,0.25)'], [0, 'rgba(255,0,0,0)', 'rgba(0,0,255,1)'],
  [0, 'hsl(120,100%,50%)', 'hsla(240,100%,50%,0.5)'],
  [0, 'hsl(0,0%,50%)', 'hsl(360,0%,100%)'], [0, 'hsla(-30,70%,30%,0.1)', '#ffaabb'],
  [0, 0, '#fff'], [0, '#fff', 0],
  [0, 'translate(10px,20px)', 'translate(50px,-10px)'],
  [0, 'translate(10px)', 'translate(50px,80px)'],
  [0, 'translate(10px,20px,30px)', 'translate(50px)'],
  [0, 5, 'translate(20px,30px)'], [0, 'translate(20px,30px)', 5],
  [0, 'blur(0px) brightness(1)', 'blur(10px) brightness(2)'],
  [0, 'M0 10L20 30', 'M20 30L50 60'], [0, 'calc(100% - 10px)', 'calc(50% + 20px)'],
  [0, '1e+12 2e-10', '3e+12 4e-10'], [0, 'red', 'blue'], [0, '', 'value'],
  [0, 'x1', 'y2'], [0, '#112233', 'translate(5px,10px)'],
  [0, '0xff', '0b1010'], [0, ' 0o17 ', '0X20000000000001'],
  [0, 'rgb(10,20,30) 5px', 'rgba(50,80,100,0.5) 20px'],
  [0, 'hsl(120,100%,50%) 2px', 'hsla(240,100%,50%,0.5) 8px'],
  [0, 'v(١,2)', 'v(٣,4)'],
  [20, '+=0xff', '*=0b10'], [5, '-=0o10', '+=0x20'],
  [20, '+=', '*= '],
  [0, 'NaN', 'nan'], [0, 'infinity', '-infinity'],
  [0, 'translate(0px,0px)', 'translate(100%,100%)'],
];
let state = 1937;
const next = () => ((state = (Math.imul(state, 1664525) + 1013904223) >>> 0) / 4294967296);
for (let index = 0; index < 24; index++) {
  if (index % 2) {
    const first = Array.from({length: 1 + index % 4}, () => (next() * 100 - 50).toFixed(6));
    const last = Array.from({length: 1 + (index + 1) % 4}, () => (next() * 100 - 50).toFixed(6));
    inputs.push([0, `v(${first.join(',')})`, `v(${last.join(',')})`]);
  } else {
    const color = () => `rgba(${Math.floor(next() * 256)},${Math.floor(next() * 256)},${Math.floor(next() * 256)},${next().toFixed(6)})`;
    inputs.push([0, color(), color()]);
  }
}
const progresses = [-0.25, 0, 1e-11, 0.1, 0.25, 0.5, 0.75, 0.9, 1 - 1e-11, 1, 1.25];
const cases = [];
const modifiers = { identity: value => value, affine: value => value * 0.5 + 2, round2: value => round(value, 2) };
for (let index = 0; index < inputs.length; index++) {
  const [base, first, last] = inputs[index];
  for (const modifier of (index < 34 ? ['identity', 'affine', 'round2'] : ['identity'])) {
    const precision = index % 3 === 0 ? 2 : 4;
    globals.precision = precision;
    const samples = progresses.map(progress => {
      const target = { value: base };
      const animation = animate(target, { value: [first, last], duration: 100, ease: () => progress, modifier: modifiers[modifier], composition: 'none', autoplay: false });
      animation.seek(50);
      const result = target.value;
      animation.cancel();
      return { progress, result, components: [...animation._head._numbers] };
    });
    cases.push({ name: `value${index}-${modifier}`, base, first, last, precision, modifier, samples });
  }
}
const decomposed = inputs.flatMap(([,first,last]) => [first,last]).map(raw => ({ raw, value: decomposeRawValue(raw, createDecomposedValueTargetObject()) }));
const cssCases = cases.map(entry => {
  globals.precision = entry.precision;
  const property = 'opacity';
  const samples = progresses.map(progress => {
    const properties = { [property]: String(entry.base) };
    const style = {
      getPropertyValue: name => properties[name] ?? '',
      setProperty: (name, value) => { properties[name] = String(value); },
    };
    Object.defineProperty(style, property, {
      enumerable: true, get: () => properties[property], set: value => { properties[property] = String(value); },
    });
    const target = { nodeType: 1, style };
    const animation = animate(target, {
      [property]: [entry.first, entry.last], duration: 100, ease: () => progress,
      modifier: modifiers[entry.modifier], composition: 'none', autoplay: false,
    });
    animation.seek(50);
    const result = properties[property];
    if (animation._head._tweenType !== 2) throw new Error('Reference did not use CSS dispatch');
    animation.cancel();
    return { progress, result };
  });
  return { ...entry, samples };
});
const numberStrings = [0, -0, 1e-7, 1e-6, 1e20, 1e21, 1.0000000000000001e18, 1.2345678901234567, -2.2250738585072014e-308, Number.MAX_VALUE, 5e-324, Infinity, -Infinity, NaN]
  .map(value => ({ value: Object.is(value, -0) ? '-0' : String(value), result: String(value) }));
const fixture = { upstreamCommit: commit, decomposed, cases, cssCases, numberStrings };
const output = JSON.stringify(fixture, null, 2) + '\n';
writeFileSync(resolve(root, 'tests/reference/value-interpolation.json'), output);
console.log(`Captured ${decomposed.length} decompositions and ${cases.length} value composition cases`);
console.log(`Fixture SHA256 ${createHash('sha256').update(output).digest('hex')}`);
