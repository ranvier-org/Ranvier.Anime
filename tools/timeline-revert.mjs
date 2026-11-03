import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {readFileSync,writeFileSync} from 'node:fs';
import {resolve,dirname} from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
import {createHash} from 'node:crypto';

const root=process.env.RANVIER_ANIME_ROOT??resolve(dirname(fileURLToPath(import.meta.url)),'..');
assert.ok(root,'Launch through build.fsx lane-reference');
const upstream=resolve(root,'upstream/anime');
const git=(...args)=>execFileSync('rtk',['proxy','git','-C',upstream,...args],{encoding:'utf8'}).trim();
const revision=git('rev-parse','HEAD');
assert.equal(revision,'2c9cf8ea00329f6768c7d7902252ed977d75ce42');
assert.equal(git('status','--porcelain'),'');
Date.now=()=>0;
const {createTimeline}=await import(pathToFileURL(resolve(upstream,'src/timeline/index.js')));
const {createTimer}=await import(pathToFileURL(resolve(upstream,'src/timer/index.js')));
const {engine}=await import(pathToFileURL(resolve(upstream,'src/engine/engine.js')));
engine.useDefaultMainLoop=false;
const specifications=[
 {name:'inferred',children:[{x:100,duration:100}]},
 {name:'explicit',children:[{x:[20,100],duration:100}]},
 {name:'overlap',children:[{x:100,duration:100},{x:200,duration:100,at:50}]},
 {name:'explicitOverlap',children:[{x:[20,100],duration:100},{x:[300,400],duration:100,at:50}]},
 {name:'keyframesAndScalar',children:[{x:[0,100,200],duration:200},{x:400,duration:100,at:75}]},
 {name:'setThenKeyframes',children:[{x:100,duration:0,at:25},{x:[200,300,400],duration:100,at:50}]},
 {name:'reversed',options:{reversed:true},children:[{x:[20,100],duration:100}]},
 {name:'alternate',options:{alternate:true,loop:1},children:[{x:[20,100],duration:100}]},
 {name:'borrowedTimer',borrow:true,children:[]},
 {name:'empty',children:[]}
];
const fixtures=[];
for(const spec of specifications){
 let value=10;
 const trace=[];
 const target={};
 Object.defineProperty(target,'x',{enumerable:true,get(){trace.push(['get',value]);return value;},set(next){value=next;trace.push(['set',next]);}});
 const callbacks={onBegin:()=>trace.push(['begin']),onBeforeUpdate:()=>trace.push(['before']),onUpdate:()=>trace.push(['update']),onRender:()=>trace.push(['render']),onPause:()=>trace.push(['pause']),onComplete:()=>trace.push(['complete'])};
 const timeline=createTimeline({autoplay:false,...spec.options,...callbacks});
 let borrowed;
 if(spec.borrow){borrowed=createTimer({duration:100,autoplay:false});timeline.sync(borrowed,0);}
 for(const child of spec.children){const {at=0,...parameters}=child;timeline.add(target,{ease:'linear',...parameters},at);}
 const construction=trace.splice(0);
 timeline.seek(75);trace.length=0;
 const before={value,time:timeline.currentTime,borrowedTime:borrowed?.currentTime??null};
 timeline.revert();
 const first={value,time:timeline.currentTime,cancelled:timeline._cancelled,paused:timeline.paused,borrowedTime:borrowed?.currentTime??null,borrowedCancelled:borrowed?._cancelled??null,trace:trace.splice(0)};
 timeline.revert();
 const second={value,time:timeline.currentTime,cancelled:timeline._cancelled,trace:trace.splice(0)};
 fixtures.push({spec,construction,before,first,second});
 borrowed?.cancel();
}
assert.equal(git('status','--porcelain'),'');
for(const fixture of fixtures){
 assert.equal(fixture.first.value,10,fixture.spec.name+' restores the original property');
 assert.equal(fixture.first.time,0,fixture.spec.name+' rewinds the root');
 assert.equal(fixture.first.cancelled,1,fixture.spec.name+' cancels the root');
 assert.equal(fixture.first.paused,true,fixture.spec.name+' stops playback');
 assert.ok(fixture.first.trace.every(event=>event[0]==='set'),'revert suppresses lifecycle callbacks');
}
const byName=new Map(fixtures.map(fixture=>[fixture.spec.name,fixture]));
assert.deepEqual(byName.get('explicit').construction,[['get',10]],'explicit from still captures original in source');
assert.deepEqual(byName.get('explicit').first.trace,[['set',20],['set',10]],'rewind precedes original restoration');
assert.deepEqual(byName.get('reversed').first.trace,[['set',100],['set',10]],'reversed rewind uses the directional endpoint before restoration');
assert.equal(byName.get('borrowedTimer').first.borrowedTime,0,'borrowed controller rewinds');
assert.equal(byName.get('borrowedTimer').first.borrowedCancelled,0,'borrowed controller retains its independent lifetime');
assert.deepEqual(byName.get('explicit').second.trace,[['set',20],['set',10]],'source repeated revert writes again; native terminal no-op is an explicit adaptation');
const result={upstreamCommit:revision,version:JSON.parse(readFileSync(resolve(upstream,'package.json'),'utf8')).version,scope:'Actual pinned timeline revert against recording numeric object getters/setters and borrowed actual Timer; traces retain construction reads, rewind writes, callbacks and reverse property restoration. Native CSS and owner-disposal behavior require separate qualification.',fixtures};
const json=JSON.stringify(result,null,2)+'\n';
writeFileSync(resolve(root,'tests/reference/timeline-revert.json'),json);
console.log('Verified '+fixtures.length+' actual timeline revert schedules; SHA256 '+createHash('sha256').update(json).digest('hex'));
