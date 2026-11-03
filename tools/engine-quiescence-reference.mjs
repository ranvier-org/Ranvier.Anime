// Anime.js MIT, Copyright (c) 2025 Julian Garnier; see canonical THIRD-PARTY-NOTICES.md.
import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {writeFileSync} from 'node:fs';
import {dirname,resolve} from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
const script=fileURLToPath(import.meta.url),lane=dirname(script),root=process.env.RANVIER_ANIME_ROOT ?? resolve(lane,'..');
assert.ok(root);const upstream=resolve(root,'upstream/anime');
const cases=['cancelOnly','cancelBoth','mixedLive','pauseBoth'];
if(process.argv[2]==='--case'){
 const name=cases[Number(process.argv[3])];assert.ok(name);
 let clock=0,id=0;Date.now=()=>clock;const pending=new Map();
 const document={hidden:false,addEventListener(){}};
 globalThis.window={document};globalThis.document=document;
 globalThis.requestAnimationFrame=callback=>{pending.set(++id,callback);return id};globalThis.cancelAnimationFrame=key=>pending.delete(key);
 const {engine}=await import(pathToFileURL(resolve(upstream,'src/engine/engine.js')));
 const {createTimer}=await import(pathToFileURL(resolve(upstream,'src/timer/timer.js')));
 const events=[],roots={};
 const make=(name,fps=240)=>roots[name]=createTimer({duration:1000,autoplay:false,frameRate:fps,onUpdate:self=>events.push({name,time:self.currentTime})});
 const first=make('A');const second=name==='cancelOnly'?null:make('B');
 first.resume();if(second)second.resume();
 const samples=[];const snapshot=stage=>samples.push({stage,clock,phase:engine._lastTickTime,events:events.splice(0),timers:Object.fromEntries(Object.entries(roots).map(([name,timer])=>[name,{time:timer.currentTime,paused:timer.paused,cancelled:timer.cancelled}])),pending:pending.size});
 const frame=after=>{clock+=after;const current=[...pending.values()];pending.clear();for(const callback of current)callback(clock)};
 frame(5);snapshot('first');
 if(name==='pauseBoth'){first.pause();second.pause()}else{first.cancel();if(name==='cancelBoth')second.cancel()}
 snapshot('stopped');clock+=100;snapshot('idleAdvance');const next=make('C',30);snapshot('constructed');next.resume();snapshot('resumed');
 for(const after of [1,1,2,17,17]){frame(after);snapshot('frame'+after)}
 for(const timer of Object.values(roots))timer.cancel();
 process.stdout.write(JSON.stringify({name,samples}));
}else{
 const git=(...args)=>execFileSync('rtk',['proxy','git','-C',upstream,...args],{encoding:'utf8'}).trim();
 assert.equal(git('rev-parse','HEAD'),'2c9cf8ea00329f6768c7d7902252ed977d75ce42');assert.equal(git('status','--porcelain'),'');
 const fixtures=cases.map((_,index)=>JSON.parse(execFileSync('rtk',['proxy',process.execPath,script,'--case',String(index)],{encoding:'utf8'})));
 assert.equal(git('status','--porcelain'),'');writeFileSync(resolve(lane,'../tests/reference/engine-quiescence-reference.json'),JSON.stringify({upstreamCommit:git('rev-parse','HEAD'),scope:'Unmodified browser engine/Timer: cancel or pause,100ms host advance with no frames, new30FPS root, dense successors. Client cancellation releases only client work; the shared engine retains bounded cleanup requests and source wake phases.',fixtures},null,2)+'\n');
 console.log(`ENGINE_QUIESCENCE_SOURCE_PASS cases=${fixtures.length} samples=${fixtures.reduce((sum,item)=>sum+item.samples.length,0)}`);
}
