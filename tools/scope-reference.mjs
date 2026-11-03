// Anime.js scope behavior oracle; upstream MIT copyright (c) 2025 Julian Garnier.
import {readFileSync,writeFileSync} from 'node:fs';
import {resolve,dirname} from 'node:path';
import {pathToFileURL,fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';
import assert from 'node:assert/strict';
const root=process.env.RANVIER_ANIME_ROOT??resolve(dirname(fileURLToPath(import.meta.url)),'..');
if(!root) throw new Error('Launch with build.fsx lane-reference');
const upstream=resolve(root,'upstream/anime');
const expected='2c9cf8ea00329f6768c7d7902252ed977d75ce42';
assert.equal(execFileSync('rtk',['proxy','git','-C',upstream,'rev-parse','HEAD'],{encoding:'utf8'}).trim(),expected);
assert.equal(execFileSync('rtk',['proxy','git','-C',upstream,'status','--porcelain'],{encoding:'utf8'}).trim(),'');
const media=new Map();
globalThis.window={matchMedia(query){
 if(!media.has(query)) media.set(query,{matches:false,listeners:new Set(),
  addEventListener(_,listener){this.listeners.add(listener)},
  removeEventListener(_,listener){this.listeners.delete(listener)},
  change(matches){this.matches=matches;for(const listener of [...this.listeners])listener.handleEvent({type:'change'})}});
 return media.get(query);
}};
globalThis.document={addEventListener(){},querySelectorAll(){return []}};
globalThis.requestAnimationFrame=()=>1;
globalThis.cancelAnimationFrame=()=>{};
globalThis.NodeList=class NodeList {};
globalThis.HTMLCollection=class HTMLCollection {};
globalThis.SVGElement=class SVGElement {};
Date.now=()=>0;
const {createScope}=await import(pathToFileURL(resolve(upstream,'src/scope/index.js')));
const {createTimer}=await import(pathToFileURL(resolve(upstream,'src/timer/index.js')));
const {animate}=await import(pathToFileURL(resolve(upstream,'src/animation/index.js')));
const cases={};
{
 const trace=[],scope=createScope();
 scope.add(s=>{
  trace.push('construct');
  s.register({revert(){trace.push('regular1')}});
  s.register({revert(){trace.push('regular2')}});
  s.addOnce(s=>{
   trace.push('once');
   s.register({revert(){trace.push('onceResource')}});
   return ()=>trace.push('onceCleanup');
  });
  return ()=>trace.push('cleanup');
 });
 scope.refresh();
 cases.refresh=trace.slice();
 scope.revert();
 cases.final=trace.slice(cases.refresh.length);
 assert.deepEqual(cases.refresh,['construct','once','regular2','regular1','cleanup','construct']);
 assert.deepEqual(cases.final,['cleanup','regular2','regular1','onceResource','onceCleanup']);
}
{
 const scope=createScope(),trace=[];
 scope.add('times',n=>{scope.register({revert(){trace.push('methodRevert')}});return n*2});
 cases.methodResult=scope.methods.times(21);
 scope.refresh();
 scope.revert();
 cases.methodTrace=trace;
 assert.equal(cases.methodResult,42);
 assert.deepEqual(trace,['methodRevert']);
}
{
 const scope=createScope({mediaQueries:{wide:'(width > 10px)'}}),trace=[];
 scope.add(s=>{trace.push(s.matches.wide)});
 const mq=media.get('(width > 10px)');
 mq.change(true);
 cases.media=trace.slice();
 scope.revert();
 cases.mediaListenerCount=mq.listeners.size;
 assert.deepEqual(cases.media,[false,true]);
 assert.equal(cases.mediaListenerCount,0);
}
{
 const scope=createScope(),created=[],trace=[];
 scope.add(s=>{
  s.keepTime(()=>{
   const timer={currentIteration:0,iterationProgress:0,reversed:false,_alternate:false,_startTime:7,
    revert(){trace.push('revert'+created.indexOf(this))}};
   created.push(timer);s.register(timer);return timer;
  });
 });
 Object.assign(created[0],{currentIteration:2,iterationProgress:.37,_startTime:100});
 scope.refresh();
 cases.keepTime={count:created.length,iteration:created[1].currentIteration,progress:created[1].iterationProgress,start:created[1]._startTime,trace:trace.slice()};
 assert.deepEqual(cases.keepTime,{count:2,iteration:2,progress:.37,start:100,trace:['revert0']});
 scope.revert();
}
cases.scopedTimers=[];
for(const spec of [
 {name:'forward',time:37},
 {name:'reverse',time:37,reversed:true},
 {name:'alternateOdd',time:125,alternate:true},
 {name:'alternateEven',time:225,alternate:true},
 {name:'reverseAlternateOdd',time:125,reversed:true,alternate:true},
 {name:'loopGap',time:112,alternate:true,loopDelay:25},
 {name:'changedDuration',time:125,nextDuration:200},
 {name:'signedIterationOverflow',time:429496729600,loop:4294967295,nextLoop:4,alternate:true},
]){
 const scope=createScope(),created=[],events=[];
 let duration=100,loop=spec.loop??4;
 const params={autoplay:false,reversed:!!spec.reversed,alternate:!!spec.alternate,loopDelay:spec.loopDelay||0};
 const take=timer=>({time:timer.currentTime,iteration:timer.currentIteration,progress:timer.iterationProgress,reversed:timer.reversed,paused:timer.paused,start:timer._startTime,duration:timer.duration});
 scope.add(s=>{s.keepTime(()=>{
  const timer=createTimer({...params,loop,duration,onBegin:()=>events.push('begin'),onPause:()=>events.push('pause'),onComplete:()=>events.push('complete')});
  created.push(timer);return timer;
 })});
 created[0].seek(spec.time);
 created[0]._startTime=73;
 const before=take(created[0]);
 events.length=0;
 duration=spec.nextDuration||100;
 loop=spec.nextLoop??loop;
 scope.refresh();
 cases.scopedTimers.push({spec,before,after:take(created[1]),events:events.slice()});
 assert.equal(created[1]._startTime,73);
 scope.revert();
}
cases.numericRevert=[];
for(const operation of ['revert','cancel','completeThenRevert']){
 const target={value:10},events=[],writes=[];
 const animation=animate(target,{value:110,duration:100,autoplay:false,ease:'linear',
  onBegin:()=>events.push('begin'),onRender:()=>events.push('render'),
  onPause:()=>events.push('pause'),onComplete:()=>events.push('complete')});
 animation.seek(50);
 const before=target.value;
 events.length=0;
 if(operation==='completeThenRevert') animation.complete();
 if(operation==='cancel') animation.cancel();else animation.revert();
 cases.numericRevert.push({operation,before,after:target.value,events:events.slice()});
 animation.cancel();
}
writeFileSync(resolve(root,'tests/reference/scope-reference.json'),JSON.stringify({upstreamCommit:expected,cases},null,2)+'\n');
console.log('Captured 4 scope behavior groups and 8 actual timer keepTime cases from clean pinned source');
