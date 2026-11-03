// Anime.js MIT, Copyright (c) 2025 Julian Garnier; see canonical THIRD-PARTY-NOTICES.md.
import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {writeFileSync} from 'node:fs';
import {dirname,resolve} from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
const lane=dirname(fileURLToPath(import.meta.url)),root=process.env.RANVIER_ANIME_ROOT ?? resolve(lane,'..');
assert.ok(root);const upstream=resolve(root,'upstream/anime');
const git=(...args)=>execFileSync('rtk',['proxy','git','-C',upstream,...args],{encoding:'utf8'}).trim();
assert.equal(git('rev-parse','HEAD'),'2c9cf8ea00329f6768c7d7902252ed977d75ce42');assert.equal(git('status','--porcelain'),'');
let now=0,id=0;Date.now=()=>now;const pending=new Map();
const document={hidden:false,addEventListener(){}};
globalThis.window={document};globalThis.document=document;
globalThis.requestAnimationFrame=callback=>{pending.set(++id,callback);return id};globalThis.cancelAnimationFrame=key=>pending.delete(key);
const {engine}=await import(pathToFileURL(resolve(upstream,'src/engine/engine.js')));
const {createTimer}=await import(pathToFileURL(resolve(upstream,'src/timer/timer.js')));
const samples=[],events=[];
let successor;
const first=createTimer({duration:1000,autoplay:false});first.resume();
const snapshot=stage=>samples.push({stage,now,phase:engine._lastTickTime,pending:pending.size,time:successor?.currentTime??null,paused:successor?.paused??null,cancelled:successor?.cancelled??null,events:events.splice(0)});
const frame=after=>{now+=after;const jobs=[...pending.values()];pending.clear();for(const job of jobs)job(now)};
frame(5);snapshot('first');first.cancel();snapshot('cancel');
for(let index=0;index<3;index++){frame(20);snapshot('idleFrame'+index)}
successor=createTimer({duration:1000,autoplay:false,frameRate:30,onUpdate:self=>events.push(self.currentTime)});snapshot('constructed');
successor.resume();snapshot('resumed');
for(const after of [1,1,2,17,17,1,1,2,17,17]){frame(after);snapshot('dense'+after)}
successor.cancel();
assert.equal(git('status','--porcelain'),'');
writeFileSync(resolve(lane,'../tests/reference/engine-idle-phase-reference.json'),JSON.stringify({upstreamCommit:git('rev-parse','HEAD'),scope:'Actual browser RAF engine: cancel sole root, deliver three idle host RAF frames, construct30FPS successor and deliver dense frames.',samples},null,2)+'\n');
console.log(`ENGINE_IDLE_PHASE_SOURCE_PASS samples=${samples.length}`);
