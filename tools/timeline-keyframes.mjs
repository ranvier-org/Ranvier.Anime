import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {readFileSync,writeFileSync} from 'node:fs';
import {resolve,dirname} from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
import {createHash} from 'node:crypto';

const script=fileURLToPath(import.meta.url),root=resolve(dirname(script),'..'),upstream=resolve(root,'upstream/anime');
const base=[
 {name:'rawFrames',children:[{id:'a',at:25,parameters:{x:[0,100,50],duration:200}}]},
 {name:'gappedFrames',children:[{id:'a',at:25,parameters:{x:[{from:0,to:100,duration:100},{to:200,duration:50,delay:100}],duration:300}}]},
 {name:'sparseRows',targets:[{x:10,y:20},{x:30,y:40}],children:[{id:'a',at:40,parameters:{duration:300,keyframes:[{x:100,duration:100},{y:200,duration:100},{x:0,y:0,duration:100}]}}]},
 {name:'sparseOffsets',children:[{id:'a',at:20,parameters:{duration:200,keyframes:{'0%':{x:0,y:0,ease:'inQuad'},'50%':{x:100,ease:'outQuad'},'100%':{x:200,y:200}}}}]},
 {name:'independentChildren',children:[{id:'a',at:0,parameters:{x:[0,100,200],duration:200}},{id:'b',at:50,parameters:{y:[0,200,100],duration:300}}]},
 {name:'overlappingChildren',children:[{id:'a',at:0,parameters:{x:[0,100,200],duration:200}},{id:'b',at:75,parameters:{x:[{to:300},{to:400}],duration:200}}]},
 {name:'reorderedChildren',children:[{id:'a',at:100,parameters:{x:[0,100,200],duration:200}},{id:'b',at:0,parameters:{x:[{to:300},{to:400}],duration:200}}]},
 {name:'modifiedPredecessors',children:[{id:'a',at:0,parameters:{x:[0,100,200],duration:200,modifier:'plusOne'}},{id:'b',at:100,parameters:{x:[{to:'+=100'},{to:'*=2'}],duration:200,modifier:'double'}}]},
 {name:'delayedLoopChild',parameters:{loop:1,alternate:true},children:[{id:'a',at:25,parameters:{x:[0,100,200],duration:200,delay:40,loop:1,loopDelay:30,alternate:true}}]},
 {name:'reverseChildAndRoot',parameters:{loop:2,alternate:true,reversed:true},children:[{id:'a',at:25,parameters:{x:[0,100,200],duration:200,reversed:true}}]},
 {name:'signedOffsetAndDelay',children:[{id:'a',at:-25,parameters:{x:[{to:100,delay:-20},{to:200,delay:10}],duration:200}}]},
 {name:'fractionalAbsoluteOffset',children:[{id:'a',at:5234.5,parameters:{x:[0,100,200,300,400],duration:4000,delay:0.125}}],times:[0,5234.5,5234.625,6234.625,7234.625,9234.625,6234.625,0]},
 {name:'zeroChild',parameters:{loop:2},children:[{id:'a',at:50,parameters:{x:[0,100],duration:0}}]},
 {name:'emptyChild',children:[{id:'a',at:25,parameters:{x:[],duration:100}}]},
 {name:'mutedResetComplete',children:[{id:'a',at:25,parameters:{x:[0,100,200],duration:200,loop:1}}],actions:[['seekMuted',50],['seekMuted',225],['reset'],['seek',50],['completeMuted'],['seek',0],['seek',125]]},
 {name:'scheduledSpeedFps',parameters:{frameRate:20,loop:1},children:[{id:'a',at:25,parameters:{x:[0,100,200],duration:200,playbackRate:2,frameRate:20}}],actions:[['resume'],['frame',1],['frame',20],['frame',25],['speed',2],['frame',100],['pause'],['frame',100],['reverse'],['frame',25],['reset'],['restart'],['frame',500]]},
 {name:'targetAwareTimeline',targets:[{x:10,y:20},{x:30,y:40}],children:[{id:'a',at:25,parameters:{x:[{to:{fn:'target',base:100,step:10},from:{fn:'index',base:0,step:10},duration:{fn:'index',base:100,step:25}},{to:{fn:'previous',base:100}}],duration:200}}]},
 {name:'previousAcrossChildren',children:[{id:'a',at:0,parameters:{x:[0,100],duration:100,modifier:'plusOne'}},{id:'b',at:100,parameters:{x:[{to:{fn:'previous',base:10}},{to:{fn:'previous',base:20}}],duration:200}}]},
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
  timeline.add(states,{ease:'linear',composition:input.composition,...convert(child.parameters,child.id),...callbacks(child.id)},child.at);
  const controller=timeline._tail;
  assert.equal(controller.parent,timeline,'actual owned timeline child');
  assert.ok(controller instanceof JSAnimation,'actual animation child');
  children.set(child.id,controller);
  construction.push({id:child.id,snapshot:snapshot(),writes:writes.splice(0),effects:effects.splice(0),events:events.splice(0),trace:trace.splice(0),
   delay:number(controller._delay),duration:number(controller.duration),iterationDuration:number(controller.iterationDuration),offset:number(controller._offset),tweens:tweens(controller)});
 }
 const programs=Object.fromEntries([...children].map(([id,child])=>[id,tweens(child)]));
 timeline.reset();
 const capture=()=>({snapshot:snapshot(),writes:writes.splice(0),effects:effects.splice(0),events:events.splice(0),trace:trace.splice(0)});
 const initial=capture(),end=timeline.duration;
 const actions=input.actions??(input.times??[-25,0,10,25,50,75,100,125,150,200,225,250,end/2,end,end+25,125,0]).map(time=>['seek',time]);
 const samples=actions.map(([operation,value])=>{
  if(operation==='frame'){now+=value;if(!timeline.paused)tick(timeline,(now-timeline._startTime)*timeline._speed,0,0,timeline._fps<engine._fps?timeline.requestTick(now):1);}
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
 assert.equal(byName.get('none-rawFrames').duration,225,'position contributes to parent duration');
 for(const fixture of fixtures)for(const stage of fixture.construction)assert.equal(stage.writes.length,0,'construction prepares caches without target writes');
 const muted=byName.get('none-mutedResetComplete');
 assert.equal(muted.samples[0].state.events.length,0,'muted owned child lifecycle is suppressed');
 const overlap=byName.get('replace-overlappingChildren').programs;
 assert.equal(overlap.a[0].changeDuration,75,'earlier tween is truncated at new child insertion');
 assert.equal(overlap.a[1].changeDuration,1e-11,'future earlier-child tween is overridden');
 assert.equal(overlap.b[0].from,75,'new child inherits the internally sampled predecessor');
 const inputs=byName.get('replace-previousAcrossChildren').construction[1].effects.filter(effect=>effect.id==='b');
 assert.deepEqual(inputs.map(effect=>effect.previous),[101,111],'modified caches chain across children and within a child');
 const ordered=byName.get('replace-modifiedPredecessors').samples.find(sample=>sample.value===50).state.trace;
 const phase=item=>item.kind==='event'?item.id+':'+item.event:item.kind;
 const expectedPhases=['root:BeforeUpdate','root:Update','a:BeforeUpdate','effect','write','a:Render','a:Update','root:Render'];
 assert.deepEqual(ordered.map(phase),expectedPhases,'effects and writes retain their exact lifecycle phase');
 const reordered=structuredClone(ordered);
 [reordered[2],reordered[3]]=[reordered[3],reordered[2]];
 assert.throws(()=>assert.deepEqual(reordered.map(phase),expectedPhases),'cross-channel order mutation is observable');
 assert.equal(git('status','--porcelain'),'');
 const text=JSON.stringify({upstreamCommit:revision,version:JSON.parse(readFileSync(resolve(upstream,'package.json'),'utf8')).version,
  scope:'Actual isolated Timeline.add animation children; normalized pre/post-overlap programs, construction/reset writes and input/modifier effects, owned child/root callbacks and self/target snapshots. Numeric object dispatch only; not native CSS/layout or independent-animation arbitration.',fixtures},null,2)+'\n';
 writeFileSync(resolve(root,'tests/reference/timeline-keyframes.json'),text);
 console.log('Captured '+fixtures.length+' owned timeline keyframe schedules; SHA256 '+createHash('sha256').update(text).digest('hex'));
}
