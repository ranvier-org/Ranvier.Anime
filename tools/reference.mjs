import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { resolve, dirname } from 'node:path';
import { eases } from '../upstream/anime/src/easings/eases/index.js';
import { cubicBezier } from '../upstream/anime/src/easings/cubic-bezier/index.js';
import { steps } from '../upstream/anime/src/easings/steps/index.js';
import { spring } from '../upstream/anime/src/easings/spring/index.js';
import { stagger } from '../upstream/anime/src/utils/stagger.js';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const provenance = JSON.parse(readFileSync(resolve(root, 'upstream-provenance.json'), 'utf8'));
const upstream = resolve(root, 'upstream/anime');
const revision = execFileSync('rtk', ['proxy', 'git', '-C', upstream, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
const changes = execFileSync('rtk', ['proxy', 'git', '-C', upstream, 'status', '--porcelain'], { encoding: 'utf8' }).trim();
if (revision !== provenance.commit || changes) throw new Error('Reference checkout must match the clean pinned upstream commit');

const times = [0, 0.001, 0.1, 0.25, 0.5, 0.75, 0.9, 0.999, 1];
const sample = (name, evaluate) => ({ name, values: times.map(time => [time, evaluate(time)]) });
const named = Object.entries(eases).flatMap(([name, evaluate]) => {
  const result = evaluate(0.5);
  return typeof result === 'number' ? [sample(name, evaluate)] : [];
});
const springParameters = [{}, { stiffness: 170, damping: 11 }, { mass: 2, stiffness: 100, damping: 10, velocity: -2 }];
const springs = springParameters.map(parameters => {
  const instance = spring(parameters);
  return { parameters, duration: instance.duration, ...sample('spring', instance.ease) };
});
const staggering = [
  { name: 'step', value: 10, parameters: {}, count: 5 },
  { name: 'center', value: 10, parameters: { from: 'center' }, count: 5 },
  { name: 'reverse', value: 10, parameters: { reversed: true }, count: 5 },
  { name: 'range', value: [0, 100], parameters: {}, count: 5 },
  { name: 'grid', value: 10, parameters: { grid: [3, 3], from: 'center' }, count: 9 },
].map(({ name, value, parameters, count }) => {
  const evaluate = stagger(value, parameters);
  const targets = Array.from({ length: count }, () => ({}));
  return { name, value, parameters, values: targets.map((target, index) => evaluate(target, index, targets)) };
});
const fixture = {
  upstreamCommit: revision,
  version: JSON.parse(readFileSync(resolve(upstream, 'package.json'), 'utf8')).version,
  named,
  curves: [sample('cubicBezier', cubicBezier(0.42, 0, 0.58, 1)), sample('stepsEnd', steps(4)), sample('stepsStart', steps(4, true))],
  springs,
  staggering,
};
if (JSON.stringify(staggering[0].values) !== '[0,10,20,30,40]') throw new Error('Incorrect stagger reference invocation');
for (const group of [...fixture.named, ...fixture.curves, ...fixture.springs]) {
  if (group.values.some(([, value]) => !Number.isFinite(value))) throw new Error(`Nonfinite sample: ${group.name}`);
}
mkdirSync(resolve(root, 'tests/reference'), { recursive: true });
writeFileSync(resolve(root, 'tests/reference/pure.json'), JSON.stringify(fixture, null, 2) + '\n');
console.log(`Captured ${named.length} named easings, 3 curves, ${springs.length} springs and ${staggering.length} stagger cases from ${revision}`);
