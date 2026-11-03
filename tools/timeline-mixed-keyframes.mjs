import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {readFileSync,writeFileSync} from 'node:fs';
import {resolve,dirname} from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
import {createHash} from 'node:crypto';

const script=fileURLToPath(import.meta.url),root=resolve(dirname(script),'..'),upstream=resolve(root,'upstream/anime');
const base=[
 {name:'scalarThenKeyframes',children:[{kind:'scalar',id:'s',at:0,parameters:{x:[0,100],duration:100}},{kind:'keyframes',id:'k',at:50,parameters:{x:[{to:200},{to:300}],duration:200}}]},
 {name:'keyframesThenScalar',children:[{kind:'keyframes',id:'k',at:0,parameters:{x:[0,100,200],duration:200}},{kind:'scalar',id:'s',at:75,parameters:{x:400,duration:100}}]},
 {name:'explicitScalarAfterKeyframes',children:[{kind:'keyframes',id:'k',at:0,parameters:{x:[0,100,200],duration:200}},{kind:'scalar',id:'s',at:75,parameters:{x:[500,600],duration:100}}]},
 {name:'futureScalarThenEarlyKeyframes',children:[{kind:'scalar',id:'s',at:100,parameters:{x:300,duration:100}},{kind:'keyframes',id:'k',at:0,parameters:{x:[{to:100},{to:200}],duration:100}}]},
 {name:'futureKeyframesThenEarlyScalar',children:[{kind:'keyframes',id:'k',at:100,parameters:{x:[0,100,200],duration:200}},{kind:'scalar',id:'s',at:0,parameters:{x:400,duration:100}}]},
 {name:'setAfterKeyframes',children:[{kind:'keyframes',id:'k',at:0,parameters:{x:[0,100,200],duration:200}},{kind:'set',id:'s',at:75,parameters:{x:400}}]},
 {name:'keyframesAfterSet',children:[{kind:'set',id:'s',at:50,parameters:{x:100}},{kind:'keyframes',id:'k',at:75,parameters:{x:[{to:200},{to:300}],duration:200}}]},
 {name:'modifiedScalarPredecessor',children:[{kind:'scalar',id:'s',at:0,parameters:{x:[0,100],duration:100,modifier:'plusOne'}},{kind:'keyframes',id:'k',at:50,parameters:{x:[{to:{fn:'previous',base:10}},{to:{fn:'previous',base:20}}],duration:200}}]},
 {name:'modifiedKeyframePredecessor',children:[{kind:'keyframes',id:'k',at:0,parameters:{x:[0,100,200],duration:200,modifier:'plusOne'}},{kind:'scalar',id:'s',at:75,parameters:{x:400,duration:100,modifier:'double'}}]},
 {name:'signedOffsetGap',children:[{kind:'scalar',id:'s',at:-25,parameters:{x:[0,100],duration:100}},{kind:'keyframes',id:'k',at:10,parameters:{x:[{to:200,delay:40},{to:300}],duration:200}}]},
 {name:'alternateRoot',parameters:{loop:1,alternate:true,loopDelay:20},children:[{kind:'scalar',id:'s',at:0,parameters:{x:[0,100],duration:100}},{kind:'keyframes',id:'k',at:75,parameters:{x:[{to:200},{to:300}],duration:200}}]},
 {name:'reversedRoot',parameters:{loop:1,alternate:true,reversed:true},children:[{kind:'keyframes',id:'k',at:0,parameters:{x:[0,100,200],duration:200,reversed:true}},{kind:'scalar',id:'s',at:75,parameters:{x:400,duration:100}}]},
 {name:'independentProperties',children:[{kind:'scalar',id:'x',at:0,parameters:{x:[0,100],duration:100}},{kind:'scalar',id:'y',at:25,parameters:{y:[0,200],duration:200}},{kind:'keyframes',id:'k',at:50,parameters:{duration:200,keyframes:[{x:200,y:300},{x:0,y:0}]}}]},
 {name:'exactBoundary',children:[{kind:'keyframes',id:'k',at:0,parameters:{x:[0,100],duration:100}},{kind:'scalar',id:'s',at:100,parameters:{x:200,duration:100}}]},
 {name:'sameTimeSet',children:[{kind:'scalar',id:'s',at:25,parameters:{x:[0,100],duration:100}},{kind:'set',id:'set',at:50,parameters:{x:400}},{kind:'keyframes',id:'k',at:50,parameters:{x:[{to:500},{to:600}],duration:100}}]},
 {name:'zeroScalar',parameters:{loop:2},children:[{kind:'keyframes',id:'k',at:0,parameters:{x:[0,100],duration:100}},{kind:'scalar',id:'s',at:50,parameters:{x:400,duration:0}}]},
 {name:'scheduledFps',parameters:{frameRate:20,loop:1},children:[{kind:'scalar',id:'s',at:0,parameters:{x:[0,100],duration:100}},{kind:'keyframes',id:'k',at:50,parameters:{x:[{to:200},{to:300}],duration:200}}],actions:[['resume'],['frame',1],['frame',20],['frame',25],['speed',2],['frame',100],['pause'],['frame',100],['reverse'],['frame',25],['reset'],['restart'],['frame',500]]},
 {name:'mutedResetComplete',children:[{kind:'keyframes',id:'k',at:0,parameters:{x:[0,100,200],duration:200}},{kind:'scalar',id:'s',at:75,parameters:{x:400,duration:100}}],actions:[['seekMuted',50],['seekMuted',125],['reset'],['seek',50],['completeMuted'],['seek',0],['seek',125]]},
 {name:'threeWayReplacement',children:[{kind:'scalar',id:'a',at:0,parameters:{x:[0,100],duration:100}},{kind:'keyframes',id:'k',at:50,parameters:{x:[{to:200},{to:300}],duration:200}},{kind:'scalar',id:'b',at:125,parameters:{x:500,duration:100}}]},
 {name:'relativeFromScalar',children:[{kind:'scalar',id:'s',at:0,parameters:{x:[0,100],duration:100}},{kind:'keyframes',id:'k',at:50,parameters:{x:[{from:'+=5',to:'+=100',duration:100}],duration:100}}]},
];
const cases=base.flatMap(input=>['none','replace'].map(composition=>({...input,name:composition+'-'+input.name,composition})));
if(process.argv[2]==='--case'){
 let now=0;Date.now=()=>now;
 const load=path=>import(pathToFileURL(resolve(upstream,path)));
 const {createTimeline}=await load('src/timeline/timeline.js');
 const {JSAnimation}=await load('src/animation/animation.js');
 const {engine}=await load('src/engine/engine.js');
 const {tick}=await load('src/core/render.js');
 engine.useDefaultMainLoop=false;
 const input=cases[Number(process.argv[3])];assert.ok(input);
 const states=structuredClone(input.targets??[{x:10,y:20}]),writes=[],effects=[],events=[],trace=[],children=new Map();
 const record=(kind,value)=>{
  (kind==='write'?writes:kind==='effect'?effects:events).push(value);
  trace.push({kind,...value});
 };
 for(const [index,target] of states.entries())for(const name of ['x','y']){
  let value=target[name];
  Object.defineProperty(target,name,{enumerable:true,get:()=>value,set:next=>{value=next;record('write',{target:index,property:name,value:next});}});
 }
 const number=value=>Number.isFinite(value)?value:String(value);
 const state=timer=>({time:number(timer.currentTime),iterationTime:number(timer.iterationCurrentTime),iteration:timer.currentIteration,
  paused:timer.paused,began:timer.began,completed:timer.completed,backwards:timer.backwards});
 let timeline;
 const snapshot=()=>({targets:structuredClone(states),root:state(timeline),children:Object.fromEntries([...children].map(([id,child])=>[id,state(child)]))});
 const callbacks=id=>Object.fromEntries(['Begin','BeforeUpdate','Render','Update','Loop','Pause','Complete'].map(event=>['on'+event,timer=>
  record('event',{id,event,self:state(timer),snapshot:timeline?snapshot():null})]));
 const convert=(value,id,key)=>{
  if(value&&typeof value==='object'&&value.fn)return(target,index,all,previous)=>{
   const previousValue=previous?previous._value:null;
   record('effect',{id,field:key,index,count:all.length,previous:previousValue,targetValue:target.x});
   const base=(value.base??0)+(value.step??0)*index;
   return value.fn==='target'?target.x+base:value.fn==='previous'?(previousValue??target.x)+base:base;
  };
  if(Array.isArray(value))return value.map(value=>convert(value,id,key));
  if(value&&typeof value==='object')return Object.fromEntries(Object.entries(value).map(([key,value])=>[key,convert(value,id,key)]));
  if(key==='modifier')return value=>{record('effect',{id,value});return input.children.find(child=>child.id===id).parameters.modifier==='plusOne'?value+1:value*2;};
  return value;
 };
 timeline=createTimeline({...input.parameters,autoplay:false,...callbacks('root')});
 const tweens=child=>{
  const result=[];
  for(let tween=child._head;tween;tween=tween._next)result.push({target:states.indexOf(tween.target),property:tween.property,
   start:number(tween._startTime),delay:number(tween._delay),updateDuration:number(tween._updateDuration),changeDuration:number(tween._changeDuration),
   absoluteStart:number(tween._absoluteStartTime),absoluteUpdateStart:number(tween._absoluteUpdateStartTime),absoluteEnd:number(tween._absoluteEndTime),
   from:number(tween._fromNumber),to:number(tween._toNumber),value:number(tween._value),hasFrom:!!tween._hasFromValue});
  return result;
 };
 const construction=[];
 for(const child of input.children){
  const parameters={ease:'linear',composition:child.kind==='keyframes'?input.composition:'replace',...convert(child.parameters,child.id),...(child.kind==='keyframes'?callbacks(child.id):{})};
  if(child.kind==='set')timeline.set(states,parameters,child.at);else timeline.add(states,parameters,child.at);
  const controller=timeline._tail;
  assert.equal(controller.parent,timeline,'actual owned timeline child');
  assert.ok(controller instanceof JSAnimation,'actual animation child');
  if(child.kind==='keyframes')children.set(child.id,controller);
  construction.push({id:child.id,snapshot:snapshot(),writes:writes.splice(0),effects:effects.splice(0),events:events.splice(0),trace:trace.splice(0),
   delay:number(controller._delay),duration:number(controller.duration),iterationDuration:number(controller.iterationDuration),offset:number(controller._offset),tweens:tweens(controller)});
 }
 const programs=Object.fromEntries([...children].map(([id,child])=>[id,tweens(child)]));
 timeline.reset();
 const capture=()=>({snapshot:snapshot(),writes:writes.splice(0),effects:effects.splice(0),events:events.splice(0),trace:trace.splice(0)});
 const initial=capture(),end=timeline.duration;
 const actions=input.actions??(input.times??[-25,0,10,25,50,75,100,125,150,200,225,250,end/2,end,end+25,125,0]).map(time=>['seek',time]);
 const samples=actions.map(([operation,value])=>{
  if(operation==='frame'){now+=value;engine.update();}
  else if(operation==='speed')timeline.speed=value;
  else if(operation==='seekMuted')timeline.seek(value,true);
  else if(operation==='completeMuted')timeline.complete(true);
  else timeline[operation](value);
  return {operation,value:value??null,state:capture()};
 });
 const output={name:input.name,input,duration:number(timeline.duration),iterationDuration:number(timeline.iterationDuration),construction,programs,initial,samples};
 timeline.cancel();now+=100;engine.update();assert.equal(engine._head,null);
 process.stdout.write(JSON.stringify(output));
}else{
 const git=(...args)=>execFileSync('rtk',['proxy','git','-C',upstream,...args],{encoding:'utf8'}).trim();
 const revision=git('rev-parse','HEAD');assert.equal(revision,'2c9cf8ea00329f6768c7d7902252ed977d75ce42');assert.equal(git('status','--porcelain'),'');
 const fixtures=cases.map((_,index)=>JSON.parse(execFileSync(process.execPath,[script,'--case',String(index)],{encoding:'utf8',maxBuffer:8*1024*1024})));
 const byName=new Map(fixtures.map(fixture=>[fixture.name,fixture]));
 for(const fixture of fixtures)for(const stage of [...fixture.construction,fixture.initial,...fixture.samples.map(sample=>sample.state)]){
  assert.equal(stage.trace.length,stage.writes.length+stage.effects.length+stage.events.length,'every observation has one shared order');
  for(const [kind,values] of [['write',stage.writes],['effect',stage.effects],['event',stage.events]])
   assert.deepEqual(stage.trace.filter(item=>item.kind===kind).map(({kind,...value})=>value),values,'ordered trace retains each channel');
 }
 assert.equal(fixtures.length,40);
 for(const fixture of fixtures)for(const stage of fixture.construction)assert.equal(stage.writes.length,0,'mixed construction remains cache-only');
 const mixed=byName.get('replace-keyframesThenScalar');
 assert.equal(mixed.construction[1].snapshot.targets[0].x,10,'scalar preparation is cache-only');
 assert.equal(git('status','--porcelain'),'');
 const text=JSON.stringify({upstreamCommit:revision,version:JSON.parse(readFileSync(resolve(upstream,'package.json'),'utf8')).version,
  scope:'Actual isolated Timeline.add animation children; normalized pre/post-overlap programs, construction/reset writes and input/modifier effects, owned child/root callbacks and self/target snapshots. Mixed scalar, set and keyframe authoring through actual owned JSAnimation children; scalar child callbacks/state are intentionally unobserved because the native scalar facade exposes no controller. Numeric object dispatch only; native layout is a separate qualification.',fixtures},null,2)+'\n';
 writeFileSync(resolve(root,'tests/reference/timeline-mixed-keyframes.json'),text);
 console.log('Captured '+fixtures.length+' mixed scalar/keyframe timeline schedules; SHA256 '+createHash('sha256').update(text).digest('hex'));
}
