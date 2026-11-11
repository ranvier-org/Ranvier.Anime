import { readFileSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import { createHash } from 'node:crypto';
import { linear } from '../upstream/anime/src/easings/linear/index.js';
import { irregular } from '../upstream/anime/src/easings/irregular/index.js';
import { eases } from '../upstream/anime/src/easings/eases/index.js';
import { cubicBezier } from '../upstream/anime/src/easings/cubic-bezier/index.js';
import { steps } from '../upstream/anime/src/easings/steps/index.js';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const source = resolve(root, 'upstream/anime');
const commit = JSON.parse(readFileSync(resolve(root, 'upstream-provenance.json'), 'utf8')).commit;
const revision = execFileSync('rtk', ['proxy', 'git', '-C', source, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
const dirty = execFileSync('rtk', ['proxy', 'git', '-C', source, 'status', '--porcelain'], { encoding: 'utf8' }).trim();
if (revision !== commit || dirty) throw new Error('Expected clean pinned source');
const encode = value => Object.is(value, -0) ? '-0' : Number.isFinite(value) ? value : String(value);
const times = [-2, -0.25, -1e-11, 0, 1e-11, 0.1, 0.2, 0.25, 0.3333333333333333, 0.5, 0.7, 0.75, 0.9, 1 - 1e-11, 1, 1 + 1e-11, 1.25, 2];
const sample = ease => times.map(time => [time, encode(ease(time))]);
const points = [
  [], [4], [0, 1], [0, 0.5, 1], [2, -1, 3, 0],
  [0, '0.8 20%', 0.2, 1], ['0 90%', 1, '2 -50%'],
  [0, '0.5 0%', 1], [0, '0.5 100%', 1],
  [0, '0.2 50%', '0.8 50%', 1],
  [0, '0.2 80%', '0.8 20%', 1],
  [0, '0.2 -25%', '0.8 125%', 1],
  [1, '-1 0%', '2 0%', 0],
];
let state = 1729;
const next = () => ((state = (Math.imul(state, 1664525) + 1013904223) >>> 0) / 4294967296);
for (let i = 0; i < 24; i++) {
  const length = 2 + Math.floor(next() * 7);
  points.push(Array.from({ length }, (_, j) => {
    const value = Math.round((next() * 6 - 3) * 1000) / 1000;
    return j && j < length - 1 && next() > 0.3 ? `${value} ${Math.round((next() * 1.5 - 0.25) * 10000) / 100}%` : value;
  }));
}
const typed = point => {
  if (typeof point === 'number') return { value: point, position: null };
  const [value, percent] = point.split(' ');
  return { value: parseFloat(value), position: percent === undefined ? null : parseFloat(percent) / 100 };
};
const linearCases = points.map((args, index) => ({ name: `linear${index}`, points: args.map(typed), samples: sample(linear(...args)) }));
const irregularCases = [];
for (const count of [0, 1, 2, 3, 10, 17]) {
  for (const randomness of [-2, 0, 0.25, 1, 2, 10]) {
    const draws = [0, 0.9999999999999999, 0.25, 0.75, 0.5];
    let calls = 0;
    const previous = Math.random;
    let ease;
    try {
      Math.random = () => draws[calls++ % draws.length];
      ease = irregular(count, randomness);
    } finally { Math.random = previous; }
    irregularCases.push({ name: `irregular-${count}-${randomness}`, count, randomness, draws, calls, samples: sample(ease) });
  }
}
const families = [];
for (const direction of ['in', 'out', 'inOut', 'outIn']) {
  for (const [family, parameters] of [
    ['Power', [-3, -0.5, 0, 0.5, 1.68, 2, 11]],
    ['Back', [-10, -1, 0, 1.7, 10]],
  ]) {
    for (const parameter of parameters)
      families.push({ name: `${direction}${family}-${parameter}`, family, direction, parameters: [parameter], samples: sample(eases[direction + (family === 'Power' ? '' : family)](parameter)) });
  }
  for (const [amplitude, period] of [[-1, -1], [0, 0], [1, 0.3], [3, 0.8], [20, 3], [10, 1e-11]]) {
    families.push({ name: `${direction}Elastic-${amplitude}-${period}`, family: 'Elastic', direction, parameters: [amplitude, period], samples: sample(eases[direction + 'Elastic'](amplitude, period)) });
  }
}
const beziers = [[0.5, 0, 0.5, 1], [0, 0, 1, 1], [0.2, -2, 0.8, 3], [-1, 2, 2, -1], [1, 0, 0, 1], [0, 1, 0, 1], [1, 0, 1, 0]]
  .map((parameters, index) => ({ name: `bezier${index}`, parameters, samples: sample(cubicBezier(...parameters)) }));
const stepCases = [1, 4, 4294967295].flatMap(count => [false, true].map(fromStart => ({ name: `steps-${count}-${fromStart}`, count, fromStart, samples: sample(steps(count, fromStart)) })));
const fixture = { upstreamCommit: commit, linear: linearCases, irregular: irregularCases, families, beziers, steps: stepCases };
const output = JSON.stringify(fixture, null, 2) + '\n';
writeFileSync(resolve(root, 'tests/reference/easing-curves.json'), output);
console.log(`Captured ${linearCases.length} linear, ${irregularCases.length} irregular, ${families.length} parameterized families, ${beziers.length} Bezier and ${stepCases.length} step cases`);
console.log(`Fixture SHA256 ${createHash('sha256').update(output).digest('hex')}`);
