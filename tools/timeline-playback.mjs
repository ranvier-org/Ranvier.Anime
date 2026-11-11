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
const load = path => import(pathToFileURL(resolve(upstream, path)));
const {createTimeline} = await load('src/timeline/timeline.js');
const {createTimer} = await load('src/timer/timer.js');
const {animate} = await load('src/animation/animation.js');
const {engine} = await load('src/engine/engine.js');
const {tick} = await load('src/core/render.js');
engine.useDefaultMainLoop = false;
const number = value => Number.isFinite(value) ? value : String(value);
const seek = time => ({op:'seek', time});
const frame = after => ({op:'frame', after});
const times = [-25,0,25,50,75,100,125,150,175,200,225,250,300,350,450,600,225,100,25,0];
const cases = [
  {name:'property', kind:'property', parameters:{}},
  {name:'propertyLoop', kind:'property', parameters:{loop:2,loopDelay:25}},
  {name:'propertyAlternate', kind:'property', parameters:{loop:2,loopDelay:25,alternate:true}},
  {name:'propertyReversed', kind:'property', parameters:{reversed:true}},
  {name:'propertyReversedAlternate', kind:'property', parameters:{loop:1,reversed:true,alternate:true}},
  {name:'overlapLoop', kind:'overlap', parameters:{loop:2}},
  {name:'overlapAlternate', kind:'overlap', parameters:{loop:2,alternate:true}},
  {name:'overlapReversed', kind:'overlap', parameters:{reversed:true}},
  {name:'setsAndCalls', kind:'sets', parameters:{loop:2}},
  {name:'setsAndCallsAlternate', kind:'sets', parameters:{loop:2,alternate:true}},
  {name:'setsAndCallsReverse', kind:'sets', parameters:{loop:1,reversed:true}},
  {name:'timerTrack', kind:'timer', parameters:{}},
  {name:'timerTrackRootLoop', kind:'timer', parameters:{loop:2}},
  {name:'timerTrackRootAlternate', kind:'timer', parameters:{loop:2,alternate:true}},
  {name:'repeatedTimerTrack', kind:'timer', parameters:{loop:1}, childParameters:{loop:1,loopDelay:25,alternate:true}},
  {name:'borrowedTimer', kind:'syncTimer', parameters:{loop:1}},
  {name:'borrowedRepeatedTimer', kind:'syncTimer', parameters:{loop:1,alternate:true},childParameters:{loop:1,loopDelay:25,alternate:true}},
  {name:'borrowedAnimation', kind:'syncAnimation', parameters:{loop:1,alternate:true},childParameters:{loop:1,loopDelay:25}},
  {name:'resumedBorrowedAnimation',kind:'syncAnimation',parameters:{},steps:[{op:'resumeBorrowed'},seek(50),seek(75),seek(0)]},
  {name:'resumedBorrowedTimer',kind:'syncTimer',parameters:{},steps:[{op:'resumeBorrowed'},seek(50),seek(75),seek(0)]},
  {name:'resumedBorrowedTimeline',kind:'syncTimeline',parameters:{},steps:[{op:'resumeBorrowed'},seek(50),seek(75),seek(0)]},
  {name:'borrowedNested', kind:'syncTimeline', parameters:{loop:1,alternate:true},childParameters:{loop:1,alternate:true}},
  {name:'rootDelay', kind:'overlap', parameters:{delay:40,loop:1}},
  {name:'rootNegativeDelay', kind:'overlap', parameters:{delay:-25,loop:1}},
  {name:'rootNegativeGap', kind:'overlap', parameters:{loop:2,loopDelay:-25}},
  {name:'rootForever', kind:'sets', parameters:{loop:true,alternate:true}},
  {name:'rootFrames', kind:'overlap', parameters:{loop:2,loopDelay:25,alternate:true}, steps:[
    {op:'resume'},frame(25),{op:'speed',value:2},frame(25),seek(100),frame(25),{op:'pause'},frame(100),
    {op:'reverse'},frame(25),{op:'play'},frame(25),{op:'restart'},frame(500)]},
  {name:'rootLowFps', kind:'timer', parameters:{frameRate:20,loop:1},steps:[
    {op:'resume'},frame(1),frame(20),frame(25),frame(1),frame(25),frame(25),frame(100)]},
  {name:'mutedTimerTrack', kind:'timer', parameters:{loop:1},steps:[
    {op:'seek',time:50,mute:true},{op:'seek',time:200,mute:true},{op:'reset'},seek(50),{op:'complete',mute:true}]},
  {name:'mutedBorrowedTimer', kind:'syncTimer', parameters:{loop:1},steps:[
    {op:'seek',time:50,mute:true},{op:'seek',time:200,mute:true},{op:'reset'},seek(50)]},
  {name:'mutedBorrowedNested', kind:'syncTimeline', parameters:{loop:1},steps:[
    {op:'seek',time:50,mute:true},{op:'seek',time:200,mute:true},{op:'reset'},seek(50)]},
  {name:'skipIterations', kind:'sets', parameters:{loop:4},steps:[0,25,475,475,25,0].map(seek)},
  {name:'skipIterationsBackwards', kind:'sets', parameters:{loop:4,alternate:true},steps:[0,25,375,475,25,0].map(seek)},
  {name:'callbackOnly', kind:'calls', parameters:{loop:2,alternate:true}},
  {name:'zeroCallback', kind:'zeroCalls', parameters:{},steps:[{op:'resume'},frame(1),seek(0),{op:'reset'},seek(1)]},
  {name:'zeroCallbackLoop', kind:'zeroCalls', parameters:{loop:2},steps:[0,1,0,1].map(seek)},
  {name:'timerDelay',kind:'timer',parameters:{loop:1},childParameters:{delay:40}},
  {name:'timerNegativeDelay',kind:'timer',parameters:{loop:1},childParameters:{delay:-20}},
  {name:'timerReverse',kind:'timer',parameters:{},childParameters:{reversed:true}},
  {name:'timerReverseAlternateRoot',kind:'timer',parameters:{loop:2,reversed:true,alternate:true},childParameters:{loop:1,delay:25,alternate:true}},
  {name:'timerFractional',kind:'timer',parameters:{loop:1,alternate:true},childParameters:{duration:25.125,delay:0.25,loop:2,loopDelay:10.5}},
  {name:'timerFastChild',kind:'timer',parameters:{loop:1},childParameters:{playbackRate:2.5}},
  {name:'timerZero',kind:'timer',parameters:{loop:2},childParameters:{duration:0}},
  {name:'timerForeverChild',kind:'timer',parameters:{},childParameters:{loop:true,alternate:true}},
  {name:'timerNegativePeriod',kind:'timer',parameters:{loop:1},childParameters:{duration:25,loop:2,loopDelay:-50}},
  {name:'timerMaximumUint',kind:'timer',parameters:{loop:1,alternate:true},childParameters:{duration:4294967295},steps:[0,25,2147483672.5,4294967320,8589934640,0].map(seek)},
  {name:'timerLowFpsChild',kind:'timer',parameters:{},childParameters:{frameRate:20},steps:[{op:'resume'},frame(1),frame(20),frame(25),frame(1),frame(25),frame(25),frame(100)]},
  {name:'rootLowFpsProperty',kind:'property',parameters:{frameRate:20},steps:[{op:'resume'},frame(1),frame(80),frame(10)]},
];

for (let index=0;index<12;index++) {
  const duration=[0.125,25,100][index%3];
  const loop=index%3;
  const loopDelay=[-5,0,12.5][index%3];
  const delay=[-10,0,20][index%3];
  const end=Math.max(0,25+delay+(duration+loopDelay)*(loop+1)-loopDelay);
  cases.push({name:'generatedTimerTrack'+index,kind:'timer',parameters:{loop:1,alternate:index%2===0,reversed:index%4===0},
    childParameters:{duration,loop,loopDelay,delay,alternate:index%3===1,reversed:index%4===1,playbackRate:[0.5,1,2][index%3]},
    steps:[-25,0,25,25+delay,25+delay+duration/2,end,end+duration/2,end*2,end,end/2,0].map(seek)});
}

const replay = ({name,kind,parameters,childParameters={},steps=times.map(seek)}) => {
  now = 0;
  const target = {value:0};
  const controllers = new Map();
  const owned = [];
  const events = [];
  let timeline;
  const state = timer => ({time:number(timer.currentTime),iterationTime:number(timer.iterationCurrentTime),
    iteration:timer.currentIteration,backwards:timer.backwards,paused:timer.paused,began:timer.began,completed:timer.completed});
  const tracks = () => {
    const result = [];
    for (let child=timeline._head; child; child=child._next) result.push(state(child));
    return result;
  };
  const snapshot = () => ({value:target.value,root:state(timeline),tracks:tracks(),
    children:Object.fromEntries([...controllers].map(([id,controller]) => [id,state(controller)]))});
  const callbacks = id => Object.fromEntries(['Begin','BeforeUpdate','Render','Update','Loop','Pause','Complete'].map(event =>
    ['on'+event,controller => events.push({id,event,self:state(controller),state:timeline ? snapshot() : null})]));
  timeline = createTimeline({...parameters,autoplay:false,...callbacks('root')});
  const add = (id,params,position) => {
    timeline.add({...params,...callbacks(id)},position);
    controllers.set(id,timeline._tail);
  };
  const addProperty = (id,params,position) => {
    timeline.add(target,{ease:'linear',...params,...callbacks(id)},position);
    controllers.set(id,timeline._tail);
  };
  const call = (id,position) => timeline.call(() => events.push({id,event:'Call',state:snapshot()}),position);
  if (kind === 'property') addProperty('a',{value:[0,100],duration:100},0);
  else if (kind === 'overlap') {
    addProperty('a',{value:[0,100],duration:100},0);
    addProperty('b',{value:200,duration:100},50);
    call('mid',75);
  } else if (kind === 'sets') {
    call('start',0);
    addProperty('a',{value:[0,100],duration:100},0);
    timeline.set(target,{value:40},50);
    call('middle',50);
    call('end',100);
  } else if (kind === 'calls') {
    call('start',0); call('middle',50); call('end',100);
  } else if (kind === 'zeroCalls') {
    call('start',0);
  } else if (kind === 'timer') {
    add('timer',{duration:100,...childParameters},25);
    call('end',timeline.iterationDuration);
  } else {
    let child;
    if (kind === 'syncTimer') child = createTimer({duration:100,...childParameters,autoplay:false,...callbacks('borrowed')});
    else if (kind === 'syncAnimation') child = animate(target,{value:[0,100],ease:'linear',duration:100,...childParameters,autoplay:false,...callbacks('borrowed')});
    else if (kind === 'syncTimeline') {
      child = createTimeline({...childParameters,autoplay:false,...callbacks('borrowed')});
      child.add(target,{value:[0,100],ease:'linear',duration:100},0);
      child.call(() => events.push({id:'nestedMid',event:'Call',state:snapshot()}),50);
    } else throw new Error('Unknown timeline kind '+kind);
    controllers.set('borrowed',child);
    owned.push(child);
    timeline.sync(child,25);
    controllers.set('sync',timeline._tail);
  }
  timeline.reset();
  const initial = snapshot();
  const samples = steps.map(step => {
    events.length = 0;
    if (step.op === 'seek') timeline.seek(step.time,step.mute);
    else if (step.op === 'resumeBorrowed') controllers.get('borrowed').resume();
    else if (step.op === 'frame') {
      now += step.after;
      engine.update();
    } else if (step.op === 'speed') timeline.speed = step.value;
    else if (step.op === 'complete') timeline.complete(step.mute);
    else timeline[step.op]();
    return {step,events:[...events],state:snapshot()};
  });
  const result = {name,kind,parameters,childParameters,duration:number(timeline.duration),iterationDuration:number(timeline.iterationDuration),initial,samples};
  timeline.cancel();
  for (const child of owned) child.cancel();
  return result;
};
const workerIndex = process.argv.indexOf('--case');
if (workerIndex >= 0) {
  const index = Number(process.argv[workerIndex + 1]);
  if (!Number.isInteger(index) || index < 0 || index >= cases.length) throw new Error('Invalid oracle case index');
  process.stdout.write(JSON.stringify(replay(cases[index])));
} else {
const fixtures = cases.map((_, index) => JSON.parse(execFileSync('rtk',
  ['proxy', process.execPath, fileURLToPath(import.meta.url), '--case', String(index)],
  {encoding:'utf8', maxBuffer:8*1024*1024})));
const text = JSON.stringify({upstreamCommit:revision,
  version:JSON.parse(readFileSync(resolve(upstream,'package.json'),'utf8')).version,
  initialization:'Explicit reset before initial snapshot; child construction callbacks are excluded. Frame actions use actual engine.update with a fresh source process per case.',fixtures},null,2)+'\n';
writeFileSync(resolve(root,'tests/reference/timeline-playback.json'),text);
console.log(`Captured ${fixtures.length} timeline playback traces; SHA256 ${createHash('sha256').update(text).digest('hex')}`);
}
