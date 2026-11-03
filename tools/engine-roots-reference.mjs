// Anime.js MIT, Copyright (c) 2025 Julian Garnier; see canonical THIRD-PARTY-NOTICES.md.
import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {writeFileSync} from 'node:fs';
import {dirname,resolve} from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
const script=fileURLToPath(import.meta.url),lane=dirname(script),root=process.env.RANVIER_ANIME_ROOT ?? resolve(lane,'..');
assert.ok(root);
const upstream=resolve(root,'upstream/anime');
const cases=[
 {name:'samePriority',other:true},
 {name:'priority',other:true,priorityA:2,priorityB:0},
 {name:'appendWithSibling',other:true,action:'append'},
 {name:'appendAtTail',other:false,action:'append'},
 {name:'insertBefore',other:true,action:'prepend'},
 {name:'pauseNext',other:true,action:'pause'},
 {name:'resumeNext',other:true,action:'resume'},
 {name:'cancelNext',other:true,action:'cancel'},
 {name:'pauseEngine',other:true,action:'enginePause'},
 {name:'resumeEngine',other:true,action:'engineResume'},
 {name:'speedDuringTraversal',other:true,action:'engineSpeed'},
 {name:'fpsDuringTraversal',other:true,action:'engineFps'},
 {name:'unitsDuringTraversal',other:true,action:'engineUnit'},
 {name:'disableDefaultLoopDuringTraversal',other:true,action:'engineManual'},
];
if(process.argv[2]==='--case'){
 let clock=0;Date.now=()=>clock;
 let id=0;const requests=new Map();
 const document={hidden:false,addEventListener(){}};
 globalThis.window={document};
 globalThis.document=document;
 globalThis.requestAnimationFrame=callback=>{requests.set(++id,callback);return id};
 globalThis.cancelAnimationFrame=key=>requests.delete(key);
 const {engine}=await import(pathToFileURL(resolve(upstream,'src/engine/engine.js')));
 const {createTimer}=await import(pathToFileURL(resolve(upstream,'src/timer/timer.js')));
 const fixture=cases[Number(process.argv[3])];assert.ok(fixture);
 const events=[];let acted=false,second;const roots=[];
 const make=(name,priority=1)=>{
  const timer=createTimer({duration:1000,autoplay:false,priority,onUpdate:self=>{
   events.push({name,time:self.currentTime});
   if(name==='A'&&!acted){
    acted=true;
    if(fixture.action==='append')make('C',1).resume();
    if(fixture.action==='prepend')make('C',0).resume();
    if(fixture.action==='pause')second.pause();
    if(fixture.action==='resume')second.pause().resume();
    if(fixture.action==='cancel')second.cancel();
    if(fixture.action==='enginePause')engine.pause();
    if(fixture.action==='engineResume')engine.pause().resume();
    if(fixture.action==='engineSpeed')engine.speed=2;
    if(fixture.action==='engineFps')engine.fps=60;
    if(fixture.action==='engineUnit')engine.timeUnit='s';
    if(fixture.action==='engineManual')engine.useDefaultMainLoop=false;
   }
  }});
  roots.push(timer);return timer;
 };
 const first=make('A',fixture.priorityA??1);if(fixture.other)second=make('B',fixture.priorityB??1);
 first.resume();if(second)second.resume();
 const samples=[1,4,5,17,17].map(after=>{
  events.length=0;clock+=after;
  const ready=[...requests.values()];requests.clear();for(const callback of ready)callback(clock);
  return {after,clock,events:[...events],paused:engine.paused,requests:requests.size};
 });
 for(const timer of roots)timer.cancel();
 process.stdout.write(JSON.stringify({...fixture,samples}));
}else{
 const git=(...args)=>execFileSync('rtk',['proxy','git','-C',upstream,...args],{encoding:'utf8'}).trim();
 assert.equal(git('rev-parse','HEAD'),'2c9cf8ea00329f6768c7d7902252ed977d75ce42');assert.equal(git('status','--porcelain'),'');
 const fixtures=cases.map((_,index)=>JSON.parse(execFileSync('rtk',['proxy',process.execPath,script,'--case',String(index)],{encoding:'utf8'})));
 assert.equal(git('status','--porcelain'),'');
 writeFileSync(resolve(lane,'../tests/reference/engine-roots-reference.json'),JSON.stringify({upstreamCommit:git('rev-parse','HEAD'),scope:'Unmodified browser engine automatic loop and real Timer with deterministic queued RAF; root ordering, mutations and engine pause during callbacks.',fixtures},null,2)+'\n');
 console.log(`ENGINE_ROOT_SOURCE_PASS cases=${fixtures.length} samples=${fixtures.reduce((n,f)=>n+f.samples.length,0)}`);
}
