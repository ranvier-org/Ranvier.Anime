// Anime.js v4.5.0, copyright (c) 2025 Julian Garnier; see LICENSE-AnimeJS.
import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {readFileSync,writeFileSync} from 'node:fs';
import {resolve,dirname} from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
const lane=dirname(fileURLToPath(import.meta.url)),root=process.env.RANVIER_ANIME_ROOT??'C:/wk/ranvier-anime',upstream=resolve(root,'upstream/anime');
const git=(...args)=>execFileSync('rtk',['proxy','git','-C',upstream,...args],{encoding:'utf8'}).trim();
const revision=git('rev-parse','HEAD');assert.equal(revision,'2c9cf8ea00329f6768c7d7902252ed977d75ce42');assert.equal(git('status','--porcelain'),'');
Date.now=()=>0;
const load=p=>import(pathToFileURL(resolve(upstream,p)));
const {createTimeline}=await load('src/timeline/timeline.js');
const {createTimer}=await load('src/timer/timer.js');
const {animate}=await load('src/animation/animation.js');
const {engine}=await load('src/engine/engine.js');
engine.useDefaultMainLoop=false;
const safe=n=>Number.isFinite(n)?n:String(n);
const state=t=>({time:safe(t.currentTime),iterationTime:safe(t.iterationCurrentTime),iteration:t.currentIteration,backwards:t.backwards,paused:t.paused,began:t.began,completed:t.completed});
const easeFn=name=>name==='square'?t=>t*t:name==='reverse'?t=>1-t:name==='overshoot'?t=>1.25*t:name==='nan'?()=>NaN:t=>t;
const cases=[];
for(const kind of ['ownedAnimation','ownedTimer','borrowedAnimation','borrowedTimer','nested','calls','set']){
 cases.push({name:kind+'Square',kind,ease:'square',parameters:{}});
 cases.push({name:kind+'RepeatAlternate',kind,ease:'square',parameters:{loop:1,loopDelay:20,alternate:true}});
 cases.push({name:kind+'Reversed',kind,ease:'square',parameters:{reversed:true}});
}
cases.push({name:'reverseCurveRawDirection',kind:'ownedAnimation',ease:'reverse',parameters:{}});
cases.push({name:'overshootCurve',kind:'ownedAnimation',ease:'overshoot',parameters:{}});
cases.push({name:'nanFallback',kind:'ownedAnimation',ease:'nan',parameters:{}});
cases.push({name:'rawConstructionOffset',kind:'constructionOffset',ease:'square',parameters:{}});
const fixtures=[];
for(const input of cases){
 let value=0,child=null,events=[],easeInputs=[];
 const target={get value(){return value;},set value(v){value=v;}};
 const rootEase=easeFn(input.ease);
 const callbacks=id=>Object.fromEntries(['Begin','BeforeUpdate','Render','Update','Loop','Pause','Complete'].map(event=>['on'+event,t=>events.push({id,event,self:state(t),value})]));
 const timeline=createTimeline({...input.parameters,autoplay:false,playbackEase:t=>{easeInputs.push(safe(t));return rootEase(t);},...callbacks('root')});
 if(input.kind==='ownedAnimation')timeline.add(target,{value:[0,100],duration:100,ease:'linear'},0);
 else if(input.kind==='constructionOffset'){
  timeline.add(target,{value:[0,100],duration:100,ease:'linear'},0);
  timeline.seek(50,true);
  timeline.add(target,{value:200,duration:100,ease:'linear'},75);
 }
 else if(input.kind==='ownedTimer'){timeline.add({duration:100,delay:10,playbackRate:2,...callbacks('child')},25);child=timeline._tail;}
 else if(input.kind==='borrowedAnimation'){child=animate(target,{value:[0,100],duration:100,ease:'linear',autoplay:false,...callbacks('child')});timeline.sync(child,25);}
 else if(input.kind==='borrowedTimer'){child=createTimer({duration:100,delay:10,autoplay:false,...callbacks('child')});timeline.sync(child,25);}
 else if(input.kind==='nested'){
  child=createTimeline({autoplay:false,playbackEase:Math.sqrt,...callbacks('child')});
  child.add(target,{value:[0,100],duration:100,ease:'linear'},0);
  child.call(()=>events.push({id:'nestedCall',event:'Call',self:state(child),value}),50);
  timeline.sync(child,25);
 }else if(input.kind==='set'){
  timeline.add(target,{value:[0,100],duration:100,ease:'linear'},0);
  timeline.set(target,{value:40},50);
 }else timeline.add({duration:100},0);
 timeline.call(()=>events.push({id:'call',event:'Call',self:state(timeline),value}),50);
 timeline.reset();
 const capture=()=>({value,root:state(timeline),child:child?state(child):null});
 const initial=capture(),constructionEaseInputs=[...easeInputs];
 const duration=timeline.duration;
 const steps=[{time:-10,mute:false},{time:0,mute:true},{time:25,mute:false},{time:50,mute:false},{time:75,mute:false},
  {time:125,mute:false},{time:150,mute:false},{time:duration/2,mute:true},{time:duration,mute:false},{time:duration+10,mute:true},{time:75,mute:true},{time:25,mute:false},{time:0,mute:false}];
 const samples=[];
 for(const step of steps){
  events=[];easeInputs=[];timeline.seek(step.time,step.mute);
  samples.push({step,state:capture(),events,easeInputs});
 }
 fixtures.push({...input,duration,iterationDuration:timeline.iterationDuration,initial,constructionEaseInputs,samples});
 timeline.cancel();child?.cancel();
}
assert.equal(git('status','--porcelain'),'');
writeFileSync(resolve(lane,'timeline-playback-ease-reference.json'),JSON.stringify({upstreamCommit:revision,version:JSON.parse(readFileSync(resolve(upstream,'package.json'))).version,
 scope:'Actual untouched JS Timeline, timer, object-animation and nested-controller traces. These are portable clock/callback/value comparisons; no DOM or native backend is modeled.',fixtures},null,2)+'\n');
console.log('Captured '+fixtures.length+' actual Timeline playbackEase traces.');

