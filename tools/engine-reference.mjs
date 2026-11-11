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
 {name:'dense',steps:[1,1,3,4,8,1,2,1,4,17].map(after=>({op:'frame',after}))},
 {name:'engine60_child30',fps:60,childFps:30,steps:[1,11,1,3,14,1,2,17,16,17].map(after=>({op:'frame',after}))},
 {name:'engine480_child240',fps:480,childFps:240,steps:[1,1,1,1,1,1,2,2].map(after=>({op:'frame',after}))},
 {name:'engine480_inheritedDefault',fps:480,inheritFps:true,steps:[1,1,1,1,1,1,2,2].map(after=>({op:'frame',after}))},
 {name:'idleLateConstructor_child30',startAfter:100,childFps:30,steps:[1,3,13,17,17].map(after=>({op:'frame',after}))},
 {name:'speed',steps:[{op:'frame',after:17},{op:'speed',value:2},{op:'frame',after:17},{op:'speed',value:.5},{op:'frame',after:17}]},
 {name:'seconds',unit:'s',steps:[{op:'frame',after:17},{op:'frame',after:33},{op:'speed',value:2},{op:'frame',after:50},{op:'unit',value:'ms'},{op:'frame',after:17}]},
 {name:'pauseResume',steps:[{op:'frame',after:17},{op:'pause'},{op:'advance',after:100},{op:'resume'},{op:'frame',after:17}]},
];
if(process.argv[2]==='--case'){
 let clock=0;Date.now=()=>clock;
 const {engine}=await import(pathToFileURL(resolve(upstream,'src/engine/engine.js')));
 const {createTimer}=await import(pathToFileURL(resolve(upstream,'src/timer/timer.js')));
 const {globals}=await import(pathToFileURL(resolve(upstream,'src/core/globals.js')));
 engine.useDefaultMainLoop=false;
 const fixture=cases[Number(process.argv[3])];assert.ok(fixture);
 if(fixture.fps)engine.fps=fixture.fps;
 if(fixture.unit)engine.timeUnit=fixture.unit;
 if(fixture.startAfter)clock+=fixture.startAfter;
 const events=[];
 const timer=createTimer({duration:fixture.unit==='s'?10:10000,...(fixture.inheritFps?{}:{frameRate:fixture.childFps||240}),onUpdate:self=>events.push({time:self.currentTime,delta:self.deltaTime})});
 const samples=fixture.steps.map(step=>{
  events.length=0;
  if(step.op==='frame'){clock+=step.after;engine.update()}
  else if(step.op==='advance')clock+=step.after;
  else if(step.op==='speed')engine.speed=step.value;
  else if(step.op==='unit')engine.timeUnit=step.value;
  else if(step.op==='pause')engine.pause();
  else if(step.op==='resume')engine.resume();
  return {step,clock,time:timer.currentTime,events:[...events],engineLastTick:engine._lastTickTime,engineDelta:engine.deltaTime,engineSpeed:engine.speed,rawSpeed:engine._speed,timeUnit:engine.timeUnit,tickThreshold:globals.tickThreshold};
 });
 timer.cancel();
 process.stdout.write(JSON.stringify({...fixture,samples}));
}else{
 const git=(...args)=>execFileSync('rtk',['proxy','git','-C',upstream,...args],{encoding:'utf8'}).trim();
 assert.equal(git('rev-parse','HEAD'),'2c9cf8ea00329f6768c7d7902252ed977d75ce42');assert.equal(git('status','--porcelain'),'');
 const fixtures=cases.map((_,index)=>JSON.parse(execFileSync('rtk',['proxy',process.execPath,script,'--case',String(index)],{encoding:'utf8'})));
 assert.equal(git('status','--porcelain'),'');
 writeFileSync(resolve(lane,'../tests/reference/engine-reference.json'),JSON.stringify({upstreamCommit:git('rev-parse','HEAD'),scope:'Unmodified engine.update, Clock and Timer; deterministic Date.now; manual engine updates. Default-loop pause is tested separately.',fixtures},null,2)+'\n');
 console.log(`ENGINE_SOURCE_PASS cases=${fixtures.length} samples=${fixtures.reduce((n,f)=>n+f.samples.length,0)}`);
}
