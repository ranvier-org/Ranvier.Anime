import {execFileSync} from 'node:child_process';
import {readFileSync, writeFileSync} from 'node:fs';
import {resolve, dirname} from 'node:path';
import {fileURLToPath, pathToFileURL} from 'node:url';
import {createHash} from 'node:crypto';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const upstream = resolve(root, 'upstream/anime');
const revision = execFileSync('git', ['-C', upstream, 'rev-parse', 'HEAD'], {encoding:'utf8'}).trim();
if (revision !== '2c9cf8ea00329f6768c7d7902252ed977d75ce42' ||
    execFileSync('git', ['-C', upstream, 'status', '--porcelain'], {encoding:'utf8'}).trim()) throw new Error('Require clean pinned Anime source');
let now = 0;
Date.now = () => now;
const {animate} = await import(pathToFileURL(resolve(upstream, 'src/animation/animation.js')));
const {engine} = await import(pathToFileURL(resolve(upstream, 'src/engine/engine.js')));
const {tick} = await import(pathToFileURL(resolve(upstream, 'src/core/render.js')));
engine.useDefaultMainLoop = false;
const number = value => Number.isFinite(value) ? value : String(value);
const seek = time => ({op:'seek', time});
const frame = after => ({op:'frame', after});
const times = [-25,0,25,75,100,110,125,150,200,225,250,300,350,225,100,25,0];
const cases = [
  {name:'linearSeek', parameters:{duration:100}},
  {name:'loopSeek', parameters:{duration:100, loop:2, loopDelay:25}},
  {name:'alternateSeek', parameters:{duration:100, loop:2, loopDelay:25, alternate:true}},
  {name:'reversedSeek', parameters:{duration:100, reversed:true}},
  {name:'reversedAlternateSeek', parameters:{duration:100, loop:2, loopDelay:25, reversed:true, alternate:true}},
  {name:'delaySeek', parameters:{duration:100, delay:40}},
  {name:'negativeDelaySeek', parameters:{duration:100, delay:-20}},
  {name:'zeroSeek', parameters:{duration:0}},
  {name:'zeroLoopSeek', parameters:{duration:0, loop:2, loopDelay:25}},
  {name:'fractionalSeek', parameters:{duration:0.125, loop:2, loopDelay:0.25}, steps:[0,0.0625,0.125,0.25,0.375,0.4375,0.5,0.75,0].map(seek)},
  {name:'quadLoop', parameters:{duration:100, loop:2, alternate:true, ease:'inQuad'}},
  {name:'customOvershootLoop', parameters:{duration:100, loop:2, alternate:true}, easing:'overshoot'},
  {name:'frames', parameters:{duration:200, loop:1, loopDelay:25}, steps:[{op:'resume'},frame(25),{op:'speed',value:2},frame(25),seek(100),frame(25),{op:'pause'},frame(100),{op:'reverse'},frame(25),{op:'play'},frame(25),{op:'restart'},frame(500)]},
  {name:'frameRate', parameters:{duration:100, frameRate:20}, steps:[{op:'resume'},frame(1),frame(20),frame(25),frame(1),frame(25),frame(25),frame(25)]},
  {name:'muted', parameters:{duration:100, loop:2, alternate:true}, steps:[{op:'seek',time:50,mute:true},{op:'seek',time:125,mute:true},{op:'seek',time:300,mute:true},{op:'reset'},seek(50),{op:'complete',mute:true}]},
  {name:'foreverSeek', parameters:{duration:100, loop:true, alternate:true}},
  {name:'zeroForeverSeek', parameters:{duration:0, loop:true}},
  {name:'fpsDuringEasing', parameters:{duration:100}, easing:'fps', steps:[{op:'resume'},frame(25),frame(25),frame(50)]},
  {name:'negativePeriod', parameters:{duration:0.125, loop:3, loopDelay:-5}},
  {name:'invertedTotal', parameters:{duration:100, loop:3, loopDelay:-150}},
  {name:'invertedDelayBounds', parameters:{duration:100, delay:-150}},
  {name:'iterationInt32Boundary', parameters:{duration:100, loop:4294967295, alternate:true},
    steps:[0,50,100,214748364700,214748364800,214748364900,429496729599,429496729600,0].map(seek)},
];
for (let index=0; index<24; index++) {
  const duration = [0.125,25,100,4294967295][index%4];
  const loop = index%4;
  const loopDelay = [-5,0,25][index%3];
  const delay = [-20,0,40][index%3];
  const total = (duration+loopDelay)*(loop+1)-loopDelay;
  cases.push({name:'generatedAnimation'+index,parameters:{duration,loop,loopDelay,delay,reversed:index%2===0,alternate:index%5<3,ease:index%2?'inQuad':'linear'},
    steps:[0,duration/2,duration,duration+loopDelay/2,(duration+loopDelay)*1.5,total,total,(duration+loopDelay)/4,0].map(seek)});
}
const fixtures = cases.map(({name, parameters, steps = times.map(seek), easing}) => {
  now = 0;
  const target = {value:0};
  const snapshot = timer => ({value:target.value,time:number(timer.currentTime),iterationTime:number(timer.iterationCurrentTime),
    iteration:timer.currentIteration,backwards:timer.backwards,paused:timer.paused,completed:timer.completed});
  const events = [];
  const callbacks = Object.fromEntries(['Begin','BeforeUpdate','Render','Update','Loop','Pause','Complete'].map(event =>
    ['on' + event, timer => events.push({event,state:snapshot(timer)})]));
  let activeAnimation;
  const ease = easing === 'overshoot' ? () => 1.25 : easing === 'fps' ? time => {
    if (activeAnimation) activeAnimation.fps = 60;
    return time;
  } : parameters.ease || 'linear';
  const animation = animate(target,{...parameters,value:[0,100],ease,autoplay:false,...callbacks});
  activeAnimation = animation;
  animation.reset();
  const initial = snapshot(animation);
  const samples = steps.map(step => {
    events.length = 0;
    if (step.op === 'seek') animation.seek(step.time,step.mute);
    else if (step.op === 'frame') {
      now += step.after;
      if (!animation.paused) tick(animation,(now-animation._startTime)*animation._speed,0,0,animation._fps < engine._fps ? animation.requestTick(now) : 1);
    } else if (step.op === 'speed') animation.speed = step.value;
    else if (step.op === 'complete') animation.complete(step.mute);
    else animation[step.op]();
    return {step,events:[...events],state:snapshot(animation)};
  });
  animation.cancel();
  return {name,parameters,easing,iterationDuration:animation.iterationDuration,duration:animation.duration,initial,samples};
});
const text = JSON.stringify({upstreamCommit:revision,version:JSON.parse(readFileSync(resolve(upstream,'package.json'),'utf8')).version,
  initialization:'Both implementations are explicitly reset before the initial snapshot; the native constructor captures accessors without writing.',fixtures},null,2)+'\n';
writeFileSync(resolve(root,'tests/reference/animation-playback.json'),text);
console.log(`Captured ${fixtures.length} animation playback traces; SHA256 ${createHash('sha256').update(text).digest('hex')}`);
