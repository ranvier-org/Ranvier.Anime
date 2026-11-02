import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { resolve, dirname } from 'node:path';
import { createHash } from 'node:crypto';
Date.now = () => 0;
const { eases } = await import('../upstream/anime/src/easings/eases/index.js');
const { cubicBezier } = await import('../upstream/anime/src/easings/cubic-bezier/index.js');
const { steps } = await import('../upstream/anime/src/easings/steps/index.js');
const { spring } = await import('../upstream/anime/src/easings/spring/index.js');
const { stagger } = await import('../upstream/anime/src/utils/stagger.js');
const { animate } = await import('../upstream/anime/src/animation/index.js');
const { createTimeline } = await import('../upstream/anime/src/timeline/index.js');
const { parseTimelinePosition } = await import('../upstream/anime/src/timeline/position.js');
const { createSeededRandom } = await import('../upstream/anime/src/utils/random.js');

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
const automatic2D = [{x: 0, y: 0}, {x: 20, y: 0}, {x: 50, y: 0}, {x: 0, y: 30}, {x: 20, y: 30}, {x: 50, y: 30}];
const automatic3D = [{x: 0, y: 0, z: 0}, {x: 10, y: 0, z: 0}, {x: 0, y: 20, z: 10}, {x: 10, y: 20, z: 20}];
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
  { name: 'randomSeedZero', value: 10, parameters: { from: 'random', seed: 0 }, count: 8 },
  { name: 'randomSeedPositive', value: [10, 110], parameters: { from: 'random', seed: 12345 }, count: 8 },
  { name: 'randomSeedNegative', value: 10, parameters: { from: 'random', seed: -14 }, count: 8 },
  { name: 'randomSeedFractional', value: 10, parameters: { from: 'random', seed: 1.75 }, count: 8 },
  { name: 'randomSeedLarge', value: 10, parameters: { from: 'random', seed: 1000000000001 }, count: 8 },
  { name: 'randomReversedEase', value: 10, parameters: { from: 'random', seed: 42, reversed: true, ease: 'inQuad' }, count: 8 },
  { name: 'jitterMagnitude', value: 10, parameters: { seed: 42, jitter: 5 }, count: 8 },
  { name: 'jitterRange', value: [10, 110], parameters: { from: 'center', seed: 42, jitter: [2, 9] }, count: 8 },
  { name: 'jitterSigned', value: 10, parameters: { grid: [3, 3], axis: 'x', from: 'center', seed: 17, jitter: [-3, 5] }, count: 9 },
  { name: 'jitterRandomOrder', value: [0, 100], parameters: { from: 'random', seed: 42, jitter: [2, 9], ease: 'inQuad', reversed: true }, count: 8 },
  { name: 'jitterMapModifier', value: 10, parameters: { from: 'random', seed: 42, jitter: 5, start: 7, map: 'reverse', modifier: 'double' }, count: 8 },
  { name: 'jitterZero', value: 10, parameters: { from: 'random', seed: 42, jitter: 0 }, count: 8 },
  { name: 'jitterSingle', value: 10, parameters: { seed: 42, jitter: [2, 9] }, count: 1 },
  { name: 'randomSingle', value: 10, parameters: { from: 'random', seed: 42 }, count: 1 },
  { name: 'jitterZeroTotal', value: 10, parameters: { seed: 42, jitter: 5, total: 0 }, count: 3 },
  { name: 'jitterShortTotal', value: 10, parameters: { seed: 42, jitter: 5, total: 2 }, count: 4 },
  { name: 'autoFirst', value: 10, parameters: { grid: true }, targets: automatic2D },
  { name: 'autoCenter', value: [10, 110], parameters: { grid: true, from: 'center' }, targets: automatic2D },
  { name: 'autoLast', value: 10, parameters: { grid: true, from: 'last' }, targets: automatic2D },
  { name: 'autoIndex', value: 10, parameters: { grid: true, from: 2 }, targets: automatic2D },
  { name: 'autoPosition', value: 10, parameters: { grid: true, from: [0.25, 0.75] }, targets: automatic2D },
  { name: 'autoSignedX', value: 10, parameters: { grid: true, from: 'center', axis: 'x' }, targets: automatic2D },
  { name: 'autoSignedY', value: 10, parameters: { grid: true, from: 'last', axis: 'y', reversed: true }, targets: automatic2D },
  { name: 'auto3D', value: 10, parameters: { grid: true, from: 'center' }, targets: automatic3D },
  { name: 'auto3DZ', value: 10, parameters: { grid: true, from: [0.5, 0.5], axis: 'z' }, targets: automatic3D },
  { name: 'auto3DPosition', value: 10, parameters: { grid: true, from: [0.5, 0.5, 0.25] }, targets: automatic3D },
  { name: 'autoRandomJitter', value: [10, 110], parameters: { grid: true, from: 'random', seed: 42, jitter: [2, 9], ease: 'inQuad', reversed: true }, targets: automatic2D },
  { name: 'autoDuplicates', value: 10, parameters: { grid: true }, targets: [{x: 5, y: 5}, {x: 5, y: 5}, {x: 15, y: 5}] },
  { name: 'autoMissing', value: 10, parameters: { grid: true, from: 'center' }, targets: [{x: 0, y: 0}, {}, {x: 100, y: 100}] },
  { name: 'autoShortTotal', value: 10, parameters: { grid: true, total: 3 }, targets: automatic2D },
  { name: 'autoLongTotal', value: 10, parameters: { grid: true, total: 9 }, targets: automatic2D },
  { name: 'autoInvalidOrigin', value: 10, parameters: { grid: true, from: 20 }, targets: automatic2D },
  { name: 'autoRectCenters', value: 10, parameters: { grid: true }, targets: [{rect: {left: 0, top: 0, width: 20, height: 10}}, {rect: {left: 20, top: 10, width: 40, height: 20}}, {rect: {left: 60, top: 30, width: 10, height: 10}}] },
].map(({ name, value, parameters, count, targets: geometry }) => {
  const resolved = { ...parameters };
  if (parameters.ease === 'spring') resolved.ease = spring();
  if (parameters.map === 'reverse') resolved.use = (_, index, total) => total - 1 - index;
  if (parameters.map === 'invalid') resolved.use = (_, index, total) => total + index;
  if (parameters.modifier === 'double') resolved.modifier = value => value * 2;
  const evaluate = stagger(value, resolved);
  const targets = geometry ? geometry.map(target => target.rect ? { getBoundingClientRect: () => ({...target.rect}) } : {...target}) : Array.from({ length: count }, () => ({}));
  return { name, value, parameters, targets: geometry, values: targets.map((target, index) => {
    const output = evaluate(target, index, targets);
    return Number.isFinite(output) ? output : String(output);
  }) };
});
const unitStaggering = [
  { name: 'pxStep', unit: 'px', first: 10, parameters: {}, count: 5 },
  { name: 'remRange', unit: 'rem', first: 1, last: 5, parameters: {from: 'center'}, count: 5 },
  { name: 'percentRange', unit: '%', first: 10, last: 100, parameters: {reversed: true}, count: 5 },
  { name: 'angleNegative', unit: 'deg', first: -15, parameters: {start: 20}, count: 5 },
  { name: 'milliseconds', unit: 'ms', first: 25, parameters: {grid: [3, 2], from: 'center'}, count: 6 },
  { name: 'secondsRandom', unit: 's', first: 0.25, last: 2, parameters: {from: 'random', seed: 42, jitter: [0.1, 0.3]}, count: 6 },
  { name: 'numberStep', unit: '', first: 0.5, parameters: {ease: 'inQuad'}, count: 5 },
].map(({name, unit, first, last, parameters, count}) => {
  const value = last === undefined ? `${first}${unit}` : [`${first}${unit}`, `${last}${unit}`];
  const evaluate = stagger(value, parameters);
  const targets = Array.from({length: count}, () => ({}));
  return {name, unit, first, last, parameters, values: targets.map((target, index) => evaluate(target, index, targets))};
});
const timelinePositions = [undefined, 0, -25, 12.5, '100', '12.5', ' 25 ', '', '0x10', '0b101', '0o17', '1e2', '\uFEFF25\uFEFF', '\u008525', '0x١٢', 'infinity', 'intro', 'unknown', '+=25', '-=20', '*=2', 'intro+=10', 'outro-=50', 'intro*=2', '<', '<<', '<+=10', '<<-=30', 'missing+=10'].map(position => {
  const timeline = {iterationDuration: 350, labels: {intro: 50, outro: 300}, _tail: {_offset: 100, _delay: 20, duration: 80}};
  const result = parseTimelinePosition(timeline, position);
  return {position, result: Number.isFinite(result) ? result : String(result)};
});
const timelineSync = [
  {name: 'gapped', secondPosition: 150},
  {name: 'overlap', secondPosition: 50},
].map(({name, secondPosition}) => {
  const a = {value: 0}, b = {value: 10};
  const first = animate(a, {value: 100, duration: 100, ease: 'linear', autoplay: false});
  const second = animate(b, {value: 90, duration: 80, ease: 'linear', autoplay: false});
  const timeline = createTimeline({autoplay: false});
  timeline.sync(first, 0).label('second', secondPosition).sync(second, 'second');
  const duration = timeline.duration;
  const samples = [0, 25, 50, 100, 125, 150, duration, 100, 25, 0, 0].map(time => {
    timeline.seek(time);
    return {time, a: a.value, b: b.value};
  });
  timeline.cancel(); first.cancel(); second.cancel();
  return {name, secondPosition, duration, samples};
});
const timelineStaggering = [
  {name: 'appendStep', value: 10, parameters: {}},
  {name: 'appendRange', value: [10, 110], parameters: {}},
  {name: 'largeRangeAppend', value: [10000000000000000, 10000000000000100], parameters: {}},
  {name: 'explicitZeroRange', value: [10, 110], parameters: {start: 0}},
  {name: 'explicitStart', value: 10, parameters: {start: 20}},
  {name: 'modifierAfterAppend', value: 10, parameters: {modifier: 'double'}},
  {name: 'randomJitterAppend', value: 10, parameters: {from: 'random', seed: 42, jitter: [2, 9]}},
].map(({name, value, parameters}) => {
  const timeline = {iterationDuration: 200, labels: {}, _tail: {_offset: 0, _delay: 0, duration: 200}};
  const resolved = {...parameters};
  if (parameters.modifier === 'double') resolved.modifier = value => value * 2;
  const evaluate = stagger(value, resolved);
  const targets = Array.from({length: 4}, () => ({}));
  return {name, value, parameters, offsets: targets.map((target, index) => evaluate(target, index, targets, null, timeline))};
});
const timelineRepeatedSync = (() => {
  const target = {value: 0};
  const child = animate(target, {value: 100, duration: 100, ease: 'linear', autoplay: false});
  const timeline = createTimeline({autoplay: false});
  timeline.sync(child, 0).sync(child, 100);
  const samples = [150, 200, 150, 125, 100, 75, 0].map(time => {
    timeline.seek(time);
    return {time, value: target.value};
  });
  timeline.cancel(); child.cancel();
  return samples;
})();
const timelineCallbacks = [
  {name: 'middleCrossings', calls: [['a', 50]], steps: [{time: 0}, {time: 49}, {time: 50}, {time: 50}, {time: 51}, {time: 100}, {time: 50}, {time: 50}, {time: 49}, {time: 50}, {time: 0}]},
  {name: 'equalTimeOrder', calls: [['a', 50], ['b', 50]], steps: [{time: 100}, {time: 0}, {time: 100}, {time: 100}]},
  {name: 'edgeCalls', calls: [['start', 0], ['end', 100]], steps: [{time: 0}, {time: 1}, {time: 100}, {time: 100}, {time: 0}, {time: 0}, {time: 1}]},
  {name: 'mutedSeek', calls: [['a', 50]], steps: [{time: 75, muted: true}, {time: 75}, {time: 0, muted: true}, {time: 75}, {time: 0}, {time: 75}]},
  {name: 'resetCalls', calls: [['a', 50]], steps: [{time: 75}, {reset: true}, {time: 75}, {reset: true}, {time: 0}]},
  {name: 'negativeCall', calls: [['negative', -10]], steps: [{time: 0}, {time: 1}, {time: -20}, {time: 0}, {time: 1}]},
].map(({name, calls, steps}) => {
  const events = [];
  const timeline = createTimeline({autoplay: false});
  timeline.add({duration: 100}, 0);
  calls.forEach(([id, position]) => timeline.call(() => events.push(id), position));
  const samples = steps.map(step => {
    events.length = 0;
    if (step.reset) timeline.reset(); else timeline.seek(step.time, step.muted || false);
    return {...step, events: [...events]};
  });
  timeline.cancel();
  return {name, calls, samples};
});
const nestedTimelines = [false, true].map(muted => {
  const target = {value: 0}, events = [];
  const child = createTimeline({autoplay: false});
  child.add(target, {value: 100, duration: 100, ease: 'linear'}, 0).call(() => events.push('child'), 50);
  const parent = createTimeline({autoplay: false}).sync(child, 25);
  const samples = [0, 50, 75, 100, 125, 75, 25, 0, 100].map(time => {
    events.length = 0;
    parent.seek(time, muted);
    return {time, value: target.value, events: [...events]};
  });
  events.length = 0;
  parent.reset();
  const resetEvents = [...events];
  parent.cancel(); child.cancel();
  return {name: muted ? 'mutedParent' : 'nested', muted, samples, resetEvents};
});
const propertyCases = [
  {name: 'sequential', tracks: [{to: 100, duration: 100, position: 0}, {to: 200, duration: 100, position: 100}]},
  {name: 'gap', tracks: [{to: 100, duration: 100, position: 0}, {to: 200, duration: 100, position: 150}]},
  {name: 'overlap', tracks: [{to: 100, duration: 100, position: 0}, {to: 200, duration: 100, position: 50}]},
  {name: 'shortTakeover', tracks: [{to: 100, duration: 200, position: 0}, {to: 200, duration: 50, position: 50}]},
  {name: 'equalStarts', tracks: [{to: 100, duration: 100, position: 0}, {to: 200, duration: 100, position: 0}]},
  {name: 'earlierInsertion', tracks: [{to: 100, duration: 100, position: 100}, {to: 200, duration: 100, position: 0}]},
  {name: 'middleInsertion', tracks: [{to: 100, duration: 100, position: 0}, {to: 200, duration: 100, position: 100}, {to: 300, duration: 100, position: 200}, {to: 50, duration: 50, position: 50}]},
  {name: 'explicitFrom', tracks: [{to: 100, duration: 100, position: 0}, {from: 300, to: 200, duration: 100, position: 150}]},
  {name: 'futureFrom', tracks: [{from: 20, to: 100, duration: 100, position: 100}]},
  {name: 'negativeStart', tracks: [{to: 100, duration: 100, position: -50}, {to: 200, duration: 100, position: 25}]},
  {name: 'setBetween', tracks: [{to: 100, duration: 100, position: 0}, {set: 40, position: 50}, {to: 200, duration: 100, position: 100}]},
  {name: 'setsOnly', tracks: [{set: 10, position: 25}, {set: 20, position: 75}]},
  {name: 'setAtZero', tracks: [{set: 10, position: 0}, {to: 100, duration: 100, position: 25}]},
  {name: 'setTinyBoundary', tracks: [{set: 10, position: 50}], times: [0, 50 - 2e-11, 50 - 1e-11, 50 - 0.5e-11, 50, 50 + 0.5e-11, 50, 50 - 0.5e-11, 50 - 1e-11, 0]},
  {name: 'setZeroTinyBoundary', tracks: [{set: 10, position: 0}], times: [-2e-11, -1e-11, -0.5e-11, 0, 0.5e-11, 1e-11, 0, -0.5e-11, -1e-11, -2e-11, 0]},
  {name: 'durationTiny14', tracks: [{to: 100, duration: 1.4e-11, position: 0}], times: [0, 7e-12, 1.4e-11, 7e-12, 0]},
  {name: 'durationTiny16', tracks: [{to: 100, duration: 1.6e-11, position: 0}], times: [0, 7e-12, 1.6e-11, 8e-12, 0]},
  {name: 'durationBelowMinimum', tracks: [{to: 100, duration: 5e-12, position: 0}], times: [-1e-11, -7e-12, -5e-12, 0, -7e-12, -1e-11]},
  {name: 'zeroAnimationDuration', tracks: [{to: 100, duration: 0, position: 0}], times: [-1e-11, -5e-12, 0, 1e-11, -5e-12, -1e-11]},
  {name: 'overflowedTweenDuration', tracks: [{to: 100, duration: 1e308, position: 0}], times: [0, 1, 100, 1e12, 100, 0]},
  {name: 'easedOverlap', tracks: [{to: 100, duration: 100, position: 0, ease: 'inQuad'}, {to: 200, duration: 100, position: 50, ease: 'outQuad'}]},
  {name: 'modifiedCapture', tracks: [{to: 100, duration: 100, position: 0, modifier: 'double'}, {to: 200, duration: 100, position: 50}]},
  {name: 'overshootCapture', tracks: [{to: 100, duration: 100, position: 0, ease: 'constantOvershoot'}, {to: 200, duration: 100, position: 50}]},
  {name: 'fractional', tracks: [{to: 1, duration: 0.1, position: 0.1}, {to: 2, duration: 0.2, position: 0.2}], times: [0, 0.1, 0.15, 0.2, 0.3, 0.4, 0.3, 0.2, 0.15, 0.1, 0]},
];
const propertyRandom = createSeededRandom(1977);
for (let index = 0; index < 32; index++) {
  const positions = [-25, 0, 25, 50, 75, 100, 150];
  const durations = [25, 50, 100, 150];
  const tracks = Array.from({length: propertyRandom(3, 6)}, () => {
    const position = positions[propertyRandom(0, positions.length - 1)];
    if (propertyRandom(0, 4) === 0) return {set: propertyRandom(-100, 300), position};
    return {to: propertyRandom(-100, 300), duration: durations[propertyRandom(0, durations.length - 1)], position,
      from: propertyRandom(0, 3) === 0 ? propertyRandom(-100, 300) : undefined,
      ease: ['linear', 'inQuad', 'outQuad'][propertyRandom(0, 2)],
      modifier: propertyRandom(0, 4) === 0 ? 'double' : undefined};
  });
  propertyCases.push({name: `generated${index}`, tracks});
}
const timelineProperties = propertyCases.map(({name, tracks, times}) => {
  const target = {value: 0};
  const timeline = createTimeline({autoplay: false});
  tracks.forEach(track => {
    if (track.set !== undefined) timeline.set(target, {value: track.set}, track.position);
    else timeline.add(target, {value: track.from === undefined ? track.to : [track.from, track.to], duration: track.duration,
      ease: track.ease === 'constantOvershoot' ? () => 1.25 : track.ease || 'linear', modifier: track.modifier === 'double' ? value => value * 2 : undefined}, track.position);
  });
  const initial = target.value, duration = timeline.duration;
  if (timeline._offset !== 0) throw new Error('Reference timeline must use the controlled zero clock');
  const samples = (times || [-25, 0, 25, 49, 50, 51, 75, 100, 125, 150, 175, 200, 250, 300, 200, 175, 150, 125, 100, 75, 50, 49, 25, 0, -25, 0]).map(time => {
    timeline.seek(time);
    return {time, value: target.value};
  });
  timeline.reset();
  const resetValue = target.value;
  timeline.cancel();
  return {name, tracks, initial, duration, samples, resetValue};
});
const fixture = {
  clock: {milliseconds: 0, description: 'Date.now is fixed before loading the unmodified source engine'},
  upstreamCommit: revision,
  version: JSON.parse(readFileSync(resolve(upstream, 'package.json'), 'utf8')).version,
  named,
  curves: [sample('cubicBezier', cubicBezier(0.42, 0, 0.58, 1)), sample('stepsEnd', steps(4)), sample('stepsStart', steps(4, true))],
  springs,
  springMutations,
  staggering,
  unitStaggering,
  timelinePositions,
  timelineSync,
  timelineStaggering,
  timelineRepeatedSync,
  timelineCallbacks,
  nestedTimelines,
  timelineProperties,
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
const fixtureText = JSON.stringify(fixture, null, 2) + '\n';
writeFileSync(resolve(root, 'tests/reference/pure.json'), fixtureText);
console.log(`Captured ${named.length} named easings, 3 curves, ${springs.length} springs, ${staggering.length} numeric stagger and ${timelineProperties.length} property cases from ${revision}`);
console.log(`Fixture SHA256 ${createHash('sha256').update(fixtureText).digest('hex')}`);
