import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { resolve, dirname } from 'node:path';
import { eases } from '../upstream/anime/src/easings/eases/index.js';
import { cubicBezier } from '../upstream/anime/src/easings/cubic-bezier/index.js';
import { steps } from '../upstream/anime/src/easings/steps/index.js';
import { spring } from '../upstream/anime/src/easings/spring/index.js';
import { stagger } from '../upstream/anime/src/utils/stagger.js';
import { animate } from '../upstream/anime/src/animation/index.js';

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
  return typeof result === 'number' ? [sample(name, evaluate)] : [sample(name, evaluate())];
});
const springCases = [
  ['default', {}],
  ['physical', { stiffness: 170, damping: 11 }],
  ['negativeVelocity', { mass: 2, stiffness: 100, damping: 10, velocity: -2 }],
  ['critical', { stiffness: 100, damping: 20 }],
  ['overdamped', { stiffness: 100, damping: 40 }],
  ['perceived', { duration: 500, bounce: 0.25, mass: 4, velocity: 2 }],
  ['overdampedPerceived', { duration: 500, bounce: -0.5 }],
  ['parameterClamps', { mass: 20000, stiffness: 20000, damping: 20000, velocity: -20000 }],
  ['negativeClamps', { mass: -1, stiffness: -1, damping: -1, velocity: 20000 }],
  ['fractionalDuration', { duration: 628.5, bounce: 0.5 }],
  ['bounceUpper', { duration: 500, bounce: 1 }],
  ['bounceLower', { duration: 500, bounce: -1 }],
];
const snapshotSpring = (instance, name) => ({
  caseName: name,
  mass: instance.mass, stiffness: instance.stiffness, damping: instance.damping, velocity: instance.velocity,
  bounce: instance.bounce, duration: instance.duration,
  solverDuration: instance.solverDuration, settlingDuration: instance.settlingDuration,
  ...sample('spring', instance.ease),
});
const springs = springCases.map(([name, parameters]) => ({ parameters, ...snapshotSpring(spring(parameters), name) }));
const mutableSpring = spring();
const springMutations = [['stiffness', 200], ['damping', 20], ['mass', 2], ['velocity', -2], ['bounce', 0.7], ['duration', 900]].map(([property, value]) => {
  mutableSpring[property] = value;
  return { property, value, ...snapshotSpring(mutableSpring, property) };
});
const staggering = [
  { name: 'step', value: 10, parameters: {}, count: 5 },
  { name: 'center', value: 10, parameters: { from: 'center' }, count: 5 },
  { name: 'reverse', value: 10, parameters: { reversed: true }, count: 5 },
  { name: 'range', value: [0, 100], parameters: {}, count: 5 },
  { name: 'grid', value: 10, parameters: { grid: [3, 3], from: 'center' }, count: 9 },
  { name: 'last', value: 10, parameters: { from: 'last' }, count: 5 },
  { name: 'evenCenter', value: 10, parameters: { from: 'center' }, count: 4 },
  { name: 'index', value: 10, parameters: { from: 2 }, count: 6 },
  { name: 'start', value: 10, parameters: { start: 7.5 }, count: 5 },
  { name: 'descendingRange', value: [100, 0], parameters: {}, count: 5 },
  { name: 'rangeOrigin', value: [10, 110], parameters: {}, count: 5 },
  { name: 'rangeStartZero', value: [10, 110], parameters: { start: 0 }, count: 5 },
  { name: 'rangeStart', value: [10, 110], parameters: { start: 20 }, count: 5 },
  { name: 'eased', value: 10, parameters: { ease: 'inQuad' }, count: 5 },
  { name: 'easedReverse', value: 10, parameters: { ease: 'inQuad', reversed: true }, count: 5 },
  { name: 'gridX', value: 10, parameters: { grid: [3, 3], from: 'center', axis: 'x' }, count: 9 },
  { name: 'gridYReverse', value: 10, parameters: { grid: [3, 3], from: 'last', axis: 'y', reversed: true }, count: 9 },
  { name: 'gridSignedEase', value: 10, parameters: { grid: [3, 3], from: 'center', axis: 'x', ease: 'inQuad' }, count: 9 },
  { name: 'gridSignedSpring', value: 10, parameters: { grid: [3, 3], from: 'center', axis: 'x', ease: 'spring' }, count: 9 },
  { name: 'gridPosition', value: 10, parameters: { grid: [3, 3], from: [0.25, 0.75] }, count: 9 },
  { name: 'grid3D', value: 10, parameters: { grid: [3, 2, 2], from: 'center' }, count: 12 },
  { name: 'grid3DZ', value: 10, parameters: { grid: [3, 2, 2], from: [0.5, 0.5, 0], axis: 'z' }, count: 12 },
  { name: 'negativeStep', value: -10, parameters: {}, count: 5 },
  { name: 'totalOverride', value: [0, 100], parameters: { total: 7 }, count: 5 },
  { name: 'mappedIndex', value: 10, parameters: { map: 'reverse' }, count: 5 },
  { name: 'invalidMappedIndex', value: 10, parameters: { map: 'invalid' }, count: 5 },
  { name: 'modifier', value: 10, parameters: { modifier: 'double' }, count: 5 },
  { name: 'singleRange', value: [10, 110], parameters: {}, count: 1 },
  { name: 'singleEased', value: 10, parameters: { ease: 'inQuad' }, count: 1 },
  { name: 'zeroTotal', value: 10, parameters: { total: 0 }, count: 3 },
  { name: 'negativeHalfRound', value: 10, parameters: { grid: [2, 1], from: [0.005, 0], axis: 'x' }, count: 2 },
  { name: 'zeroMaximumAxisRange', value: [0, 100], parameters: { grid: [3, 1], from: 'last', axis: 'x' }, count: 3 },
  { name: 'zeroMaximumAxisRangeReverse', value: [0, 100], parameters: { grid: [3, 1], from: 'last', axis: 'x', reversed: true }, count: 3 },
  { name: 'gridSignedBounce', value: 10, parameters: { grid: [101, 1], from: 1, axis: 'x', ease: 'inBounce' }, count: 101 },
].map(({ name, value, parameters, count }) => {
  const resolved = { ...parameters };
  if (parameters.ease === 'spring') resolved.ease = spring();
  if (parameters.map === 'reverse') resolved.use = (_, index, total) => total - 1 - index;
  if (parameters.map === 'invalid') resolved.use = (_, index, total) => total + index;
  if (parameters.modifier === 'double') resolved.modifier = value => value * 2;
  const evaluate = stagger(value, resolved);
  const targets = Array.from({ length: count }, () => ({}));
  return { name, value, parameters, values: targets.map((target, index) => {
    const output = evaluate(target, index, targets);
    return Number.isFinite(output) ? output : String(output);
  }) };
});
const fixture = {
  upstreamCommit: revision,
  version: JSON.parse(readFileSync(resolve(upstream, 'package.json'), 'utf8')).version,
  named,
  curves: [sample('cubicBezier', cubicBezier(0.42, 0, 0.58, 1)), sample('stepsEnd', steps(4)), sample('stepsStart', steps(4, true))],
  springs,
  springMutations,
  staggering,
  timing: [0.5, 1, 4294967295, 1000000000001].map(duration => {
    const target = { value: 0 };
    const animation = animate(target, { value: 100, duration, ease: 'linear', autoplay: false });
    const actualDuration = animation.duration;
    const sampleTime = duration === 0.5 ? 0.25 : 0;
    animation.seek(sampleTime);
    const sampleValue = target.value;
    animation.cancel();
    return { duration, actualDuration, sampleTime, sampleValue };
  }),
  customEasing: (() => {
    const target = { value: 0 };
    const animation = animate(target, { value: 100, duration: 100, ease: () => 1.25, autoplay: false });
    const initial = target.value;
    animation.seek(50);
    const midpoint = target.value;
    animation.seek(100);
    const endpoint = target.value;
    animation.cancel();
    return { name: 'constantOvershoot', initial, midpoint, endpoint };
  })(),
};
if (fixture.timing[0].sampleValue !== 50) throw new Error('Fractional-duration reference did not reach its midpoint');
if (JSON.stringify(staggering[0].values) !== '[0,10,20,30,40]') throw new Error('Incorrect stagger reference invocation');
for (const group of [...fixture.named, ...fixture.curves, ...fixture.springs]) {
  if (group.values.some(([, value]) => !Number.isFinite(value))) throw new Error(`Nonfinite sample: ${group.name}`);
}
mkdirSync(resolve(root, 'tests/reference'), { recursive: true });
writeFileSync(resolve(root, 'tests/reference/pure.json'), JSON.stringify(fixture, null, 2) + '\n');
console.log(`Captured ${named.length} named easings, 3 curves, ${springs.length} springs and ${staggering.length} stagger cases from ${revision}`);
