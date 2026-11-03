import {execFileSync} from 'node:child_process';
import {readFileSync, writeFileSync} from 'node:fs';
import {resolve, dirname} from 'node:path';
import {fileURLToPath, pathToFileURL} from 'node:url';
import {createHash} from 'node:crypto';

const root = process.env.RANVIER_ANIME_ROOT ?? resolve(dirname(fileURLToPath(import.meta.url)), '..');
const scheduledCaseIndex = process.argv.indexOf('--scheduled-case');
const scheduledCaseName = scheduledCaseIndex < 0 ? null : process.argv[scheduledCaseIndex + 1];
if (scheduledCaseIndex >= 0 && !scheduledCaseName) throw new Error('Missing scheduled case name');
const upstream = resolve(root, 'upstream/anime');
const revision = execFileSync('git', ['-C', upstream, 'rev-parse', 'HEAD'], {encoding: 'utf8'}).trim();
if (revision !== '2c9cf8ea00329f6768c7d7902252ed977d75ce42' ||
    execFileSync('git', ['-C', upstream, 'status', '--porcelain'], {encoding: 'utf8'}).trim()) {
  throw new Error('Timer oracle requires the clean pinned source');
}
let clockNow = 0;
Date.now = () => clockNow;
const {createTimer} = await import(pathToFileURL(resolve(upstream, 'src/timer/timer.js')));
const {engine} = await import(pathToFileURL(resolve(upstream, 'src/engine/engine.js')));
const {tick} = await import(pathToFileURL(resolve(upstream, 'src/core/render.js')));
engine.useDefaultMainLoop = false;
const number = value => Number.isFinite(value) ? value : String(value);
const snapshot = timer => ({
  time: number(timer.currentTime), raw: number(timer._currentTime), iterationTime: number(timer.iterationCurrentTime),
  rawIterationTime: number(timer._iterationTime), iteration: timer.currentIteration, progress: number(timer.progress),
  iterationProgress: number(timer.iterationProgress), backwards: timer.backwards, reversed: timer.reversed,
  began: timer.began, completed: timer.completed, paused: timer.paused, deltaTime: number(timer.deltaTime), speed: timer.speed,
});
const seek = (time, mute = false) => ({op: 'seek', time, mute});
const tickAt = (time, mode = 1, mute = false) => ({op: 'tick', time, mode, mute});
const ordinary = [seek(-20), seek(0), seek(1), seek(50), seek(100), seek(100), seek(25), seek(0), seek(100, true), seek(100)];
const cases = [
  {name: 'seekCallbacks', parameters: {duration: 100}, steps: ordinary},
  {name: 'delayedCallbacks', parameters: {duration: 100, delay: 40}, steps: ordinary},
  {name: 'negativeDelayCallbacks', parameters: {duration: 100, delay: -20}, steps: ordinary},
  {name: 'loopCallbacks', parameters: {duration: 100, loop: 3, loopDelay: 25}, steps: [seek(1), seek(125), seek(375), seek(200), seek(475), seek(0)]},
  {name: 'alternateCallbacks', parameters: {duration: 100, loop: 2, alternate: true}, steps: [seek(1), seek(100), seek(150), seek(200), seek(300), seek(250), seek(0)]},
  {name: 'reversedCallbacks', parameters: {duration: 100, reversed: true}, steps: ordinary},
  {name: 'zeroCallbacks', parameters: {duration: 0}, steps: [seek(-1), seek(0), seek(1e-12), seek(1e-11), seek(1), seek(0)]},
  {name: 'zeroLoopCallbacks', parameters: {duration: 0, loop: 2}, steps: [seek(0), seek(1e-12), seek(1e-11), seek(1), seek(0)]},
  {name: 'zeroGapCallbacks', parameters: {duration: 0, loop: 2, loopDelay: 25}, steps: [seek(0), seek(1), seek(25), seek(49), seek(50), seek(0)]},
  {name: 'resetControls', parameters: {duration: 100}, steps: [seek(50), {op:'reset'}, seek(1), {op:'restart'}, tickAt(25), {op:'pause'}, {op:'pause'}, {op:'resume'}, tickAt(100)]},
  {name: 'directionControls', parameters: {duration: 100}, steps: [seek(25), {op:'alternate'}, {op:'alternate'}, {op:'reverse'}, tickAt(50), {op:'play'}, tickAt(75), {op:'pause'}]},
  {name: 'alternateEvenControls', parameters: {duration: 100, loop: 1, alternate: true}, steps: [seek(25), {op:'alternate'}, {op:'reverse'}, {op:'play'}, {op:'reset'}]},
  {name: 'alternateGapControls', parameters: {duration: 100, loop: 2, loopDelay: 25, alternate: true}, steps: [seek(140), {op:'reverse'}, {op:'alternate'}, {op:'play'}, {op:'reset'}]},
  {name: 'reversedReset', parameters: {duration: 100, reversed: true}, steps: [{op:'play'}, tickAt(25), {op:'reset'}, {op:'restart'}, tickAt(100)]},
  {name: 'tickCompletion', parameters: {duration: 100}, steps: [{op:'resume'}, tickAt(25), tickAt(100), tickAt(100), tickAt(50), tickAt(100)]},
  {name: 'mutedTicks', parameters: {duration: 100, loop: 2}, steps: [tickAt(50, 1, true), tickAt(125, 1, true), tickAt(300, 1, true), tickAt(0), tickAt(1), tickAt(300)]},
  {name: 'skipAndForce', parameters: {duration: 100}, steps: [tickAt(25, 0), tickAt(50, 2), tickAt(100, 0), tickAt(0, 0), tickAt(25, 2)]},
  {name: 'speedControls', parameters: {duration: 100}, steps: [seek(25), {op:'speed', value:2}, {op:'resume'}, tickAt(50), {op:'speed', value:0}, {op:'speed', value:-2}, {op:'speed', value:0.5}, {op:'pause'}]},
  {name: 'completeControl', parameters: {duration: 100}, steps: [{op:'resume'}, tickAt(25), {op:'complete'}]},
  {name: 'completeMuted', parameters: {duration: 100}, steps: [{op:'resume'}, tickAt(25), {op:'complete', mute:true}]},
  {name: 'foreverDirection', parameters: {duration: 100, loop:true, alternate:true}, steps: [seek(125), {op:'alternate'}, {op:'reverse'}, {op:'play'}, tickAt(250), {op:'pause'}]},
  {name: 'zeroResume', parameters: {duration: 0}, steps: [{op:'resume'}, {op:'reset'}, {op:'restart'}, {op:'pause'}]},
  {name: 'zeroRepeatedResume', parameters: {duration: 0, loop:2, alternate:true}, steps: [{op:'resume'}, {op:'reset'}, {op:'restart'}, {op:'pause'}]},
  {name: 'zeroDelayedResume', parameters: {duration: 0, delay:40}, steps: [{op:'resume'}, tickAt(1), {op:'reset'}, {op:'restart'}, {op:'pause'}]},
  {name: 'negativeLoopGap', parameters: {duration:100, loop:2, loopDelay:-25}, steps: [seek(1), seek(75), seek(150), seek(250), seek(0), {op:'reverse'}, {op:'reset'}]},
  {name: 'invertedBounds', parameters: {duration:100, delay:-150, loop:3, loopDelay:-150}, steps: [seek(-200), seek(0), seek(100), {op:'reset'}]},
  {name: 'unboundedCallbacks', parameters: {}, steps: [seek(1), seek(100), {op:'resume'}, tickAt(1000), {op:'pause'}, {op:'reset'}]},
];
for (let index = 0; index < 24; index++) {
  const duration = [0.125, 25, 100, 4294967295][index % 4];
  const loop = index % 3;
  const loopDelay = [0, 0.25, 25, -10][index % 4];
  const delay = [-20, 0, 40][index % 3];
  const total = (duration + loopDelay) * (loop + 1) - loopDelay;
  cases.push({name:`generatedControl${index}`, parameters:{duration, loop, loopDelay, delay, reversed:index % 2 === 0, alternate:index % 5 < 2},
    steps:[seek(0), seek(total / 3), {op:'alternate'}, {op:'pause'}, {op:'reverse'}, tickAt(total / 2),
      {op:'speed', value:0.5}, {op:'play'}, seek(total, true), {op:'reset'}, {op:'restart'}, tickAt(total), {op:'pause'}]});
}
const fixtures = scheduledCaseName ? [] : cases.map(({name, parameters, steps}) => {
  const events = [];
  const callbacks = Object.fromEntries(['Begin','BeforeUpdate','Update','Loop','Pause','Complete'].map(event =>
    ['on' + event, timer => events.push({event, state:snapshot(timer)})]));
  const timer = createTimer({...parameters, autoplay:false, ...callbacks});
  const initial = snapshot(timer);
  const samples = steps.map(step => {
    events.length = 0;
    if (step.op === 'seek') timer.seek(step.time, step.mute);
    else if (step.op === 'tick') tick(timer, step.time + timer._delay, +step.mute, 0, step.mode);
    else if (step.op === 'speed') timer.speed = step.value;
    else if (step.op === 'complete') timer.complete(step.mute);
    else timer[step.op]();
    return {step, events:[...events], state:snapshot(timer)};
  });
  timer.cancel();
  return {name, parameters, initial, samples};
});
const frame = after => ({op:'frame', after});
const scheduledCases = [
  {name:'stretchPaused', parameters:{duration:100}, steps:[seek(75),{op:'stretch',value:200},seek(100),{op:'stretch',value:50},seek(50)]},
  {name:'stretchRunning', parameters:{duration:100}, steps:[{op:'resume'},frame(25),{op:'stretch',value:200},frame(25),frame(150)]},
  {name:'stretchDelayed', parameters:{duration:100,delay:40}, steps:[{op:'resume'},frame(10),{op:'stretch',value:200},frame(25),frame(100),{op:'reset'},{op:'resume'},frame(25)]},
  {name:'stretchNegativeDelay', parameters:{duration:100,delay:-20}, steps:[seek(25),{op:'stretch',value:250},{op:'resume'},frame(25),{op:'stretch',value:100},frame(25)]},
  {name:'stretchLoops', parameters:{duration:100,loop:2,loopDelay:25,alternate:true}, steps:[seek(175),{op:'stretch',value:700},seek(300),{op:'resume'},frame(25),{op:'stretch',value:350},frame(25),{op:'reverse'},frame(100)]},
  {name:'stretchZeroThenRestore', parameters:{duration:100,delay:40,loop:2,loopDelay:25}, steps:[seek(50),{op:'stretch',value:0},{op:'resume'},{op:'stretch',value:100},{op:'reset'},{op:'resume'},frame(25)]},
  {name:'stretchTiny', parameters:{duration:100}, steps:[seek(50),{op:'stretch',value:1.4e-11},{op:'stretch',value:1.6e-11},{op:'resume'},frame(5)]},
  {name:'stretchFractional', parameters:{duration:0.125,loop:3,loopDelay:0.25,delay:0.125}, steps:[seek(0.0625),{op:'stretch',value:0.875},seek(0.1),{op:'stretch',value:1.125},{op:'resume'},frame(5)]},
  {name:'stretchCompleted', parameters:{duration:100}, steps:[seek(100),{op:'stretch',value:200},frame(25),{op:'resume'},frame(25),{op:'stretch',value:200},frame(100)]},
  {name:'stretchReversed', parameters:{duration:100,reversed:true,loop:1,alternate:true}, steps:[seek(75),{op:'stretch',value:400},{op:'resume'},frame(25),{op:'reset'},{op:'play'},frame(25)]},
  {name:'stretchUint', parameters:{duration:100}, steps:[seek(25),{op:'stretch',value:4294967295},{op:'resume'},frame(2147483647),{op:'stretch',value:100},frame(25)]},
  {name:'stretchInfiniteIterations', parameters:{duration:100,loop:true,loopDelay:25}, steps:[seek(175),{op:'stretch',value:1000},{op:'resume'},frame(25),{op:'alternate'},frame(25)]},
  {name:'stretchNegativeTotal', parameters:{duration:100,loop:3,loopDelay:-150,delay:20}, steps:[seek(25),{op:'stretch',value:200},seek(100),{op:'resume'},frame(25)]},
  {name:'stretchIdempotent', parameters:{duration:100,delay:40,loop:1,loopDelay:25}, steps:[{op:'resume'},frame(25),{op:'stretch',value:225},frame(25),{op:'pause'},{op:'stretch',value:225},seek(50)]},
  {name:'stretchLargeFinite', parameters:{duration:100,delay:40}, steps:[seek(25),{op:'stretch',value:Number.MAX_VALUE},{op:'resume'},frame(25)]},
  {name:'stretchUnboundedIteration', parameters:{}, steps:[seek(25),{op:'stretch',value:200},{op:'resume'},frame(25)]},
  {name:'scheduledBasic', parameters:{duration:100}, steps:[{op:'resume'}, frame(0), frame(25), frame(75), frame(25)]},
  {name:'scheduledPause', parameters:{duration:100}, steps:[{op:'resume'}, frame(25), {op:'advance', after:10}, {op:'pause'}, frame(200), {op:'resume'}, frame(25)]},
  {name:'scheduledSpeed', parameters:{duration:200}, steps:[{op:'resume'}, frame(25), {op:'advance', after:10}, {op:'speed', value:2}, frame(25), {op:'speed', value:0.5}, frame(20), seek(100), frame(20)]},
  {name:'scheduledDirections', parameters:{duration:100, loop:2, loopDelay:25, alternate:true}, steps:[{op:'resume'}, frame(25), {op:'alternate'}, frame(25), {op:'reverse'}, frame(25), {op:'play'}, frame(25), {op:'reset'}, {op:'restart'}, frame(500)]},
  {name:'scheduledDelay', parameters:{duration:100, delay:40}, steps:[{op:'resume'}, frame(10), frame(10), frame(10), frame(10), frame(100), seek(0), frame(25)]},
  {name:'scheduledNegativeDelay', parameters:{duration:100, delay:-20}, steps:[{op:'resume'}, frame(0), frame(25), frame(100), {op:'restart'}, frame(25)]},
  {name:'scheduledRate', parameters:{duration:100, frameRate:20}, steps:[{op:'resume'}, frame(1), frame(20), frame(25), frame(1), frame(25), frame(25), frame(25)]},
  {name:'scheduledRateChange', parameters:{duration:300, frameRate:20}, steps:[{op:'resume'}, frame(20), {op:'fps',value:60}, frame(1), frame(15), frame(1), {op:'fps',value:240}, frame(1), {op:'fps',value:0}, frame(100)]},
  {name:'scheduledZero', parameters:{duration:0}, steps:[{op:'resume'}, frame(100), {op:'reset'}, {op:'restart'}, frame(100)]},
  {name:'scheduledZeroGap', parameters:{duration:0, loop:2, loopDelay:25}, steps:[{op:'resume'}, frame(1), frame(12), frame(25)]},
  {name:'scheduledFractional', parameters:{duration:0.125, loop:3, loopDelay:0.25}, steps:[seek(0.1), {op:'speed',value:0.01}, {op:'resume'}, frame(1), frame(10), frame(100)]},
  {name:'scheduledUint', parameters:{duration:4294967295}, steps:[{op:'resume'}, frame(2147483647), {op:'speed',value:2}, frame(1073741824)]},
  {name:'scheduledForeverRollover', parameters:{duration:100, loop:true, alternate:true}, steps:[{op:'resume'}, frame(50), frame(1000000000000), frame(25), {op:'speed',value:2}, frame(500000000000), frame(25)]},
  {name:'scheduledComplete', parameters:{duration:100}, steps:[{op:'resume'}, frame(25), {op:'complete'}]},
  {name:'scheduledCurrentTime', parameters:{duration:100}, steps:[{op:'resume'}, frame(25), {op:'currentTime',value:50}, frame(25), {op:'pause'}, {op:'currentTime',value:20}, frame(50)]},
  {name:'scheduledProgress', parameters:{duration:100, loop:2, loopDelay:25}, steps:[{op:'resume'}, frame(25), {op:'progress',value:0.5}, frame(25), {op:'iterationProgress',value:0.25}, frame(10), {op:'pause'}]},
  {name:'scheduledIterationSetters', parameters:{duration:100, loop:2, loopDelay:25}, steps:[{op:'currentIteration',value:1}, {op:'resume'}, frame(25), {op:'iterationCurrentTime',value:50}, frame(25), {op:'currentIteration',value:20}, {op:'pause'}]},
  {name:'scheduledReversedSetter', parameters:{duration:100}, steps:[{op:'reversed',value:true}, frame(25), {op:'reversed',value:false}, frame(25), {op:'pause'}]},
];
const runScheduled = ({name, parameters, steps}) => {
  const events = [];
  const capture = timer => ({...snapshot(timer), fps:timer.fps,
    ...(name.startsWith('stretch') ? {duration:number(timer.duration),iterationDuration:number(timer.iterationDuration),delay:number(timer._delay),loopDelay:number(timer._loopDelay)} : {})});
  const callbacks = Object.fromEntries(['Begin','BeforeUpdate','Update','Loop','Pause','Complete'].map(event =>
    ['on' + event, timer => events.push({event, state:capture(timer)})]));
  const timer = createTimer({...parameters, autoplay:false, ...callbacks});
  const initial = capture(timer);
  const samples = steps.map(step => {
    events.length = 0;
    if (step.op === 'frame' || step.op === 'advance') {
      clockNow += step.after;
      if (step.op === 'frame') engine.update();
    } else if (step.op === 'seek') timer.seek(step.time, step.mute);
    else if (['speed','fps','currentTime','progress','iterationProgress','iterationCurrentTime','currentIteration','reversed'].includes(step.op)) timer[step.op] = step.value;
    else if (step.op === 'complete') timer.complete(step.mute);
    else if (step.op === 'stretch') timer.stretch(step.value);
    else timer[step.op]();
    return {step, events:[...events], state:capture(timer)};
  });
  timer.cancel();
  return {name, parameters, initial, samples};
};
if (scheduledCaseName !== null) {
  const selected = scheduledCases.find(({name}) => name === scheduledCaseName);
  if (!selected) throw new Error(`Unknown scheduled case: ${scheduledCaseName}`);
  process.stdout.write(JSON.stringify(runScheduled(selected)));
  process.exit(0);
}
const scheduled = scheduledCases.map(({name}) => JSON.parse(execFileSync('rtk',
  ['proxy', process.execPath, fileURLToPath(import.meta.url), '--scheduled-case', name], {encoding:'utf8'})));
const fixture = {upstreamCommit:revision, version:JSON.parse(readFileSync(resolve(upstream,'package.json'),'utf8')).version, fixtures, scheduled};
const text = JSON.stringify(fixture,null,2) + '\n';
writeFileSync(process.env.RANVIER_ANIME_TIMER_CONTROLS_OUTPUT ?? resolve(root,'tests/reference/timer-controls.json'),text);
console.log(`Captured ${fixtures.length} timer control and ${scheduled.length} scheduled traces; SHA256 ${createHash('sha256').update(text).digest('hex')}`);
