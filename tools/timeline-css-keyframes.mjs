import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {readFileSync,writeFileSync} from 'node:fs';
import {resolve,dirname} from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
import {createHash} from 'node:crypto';

const script=fileURLToPath(import.meta.url),root=resolve(dirname(script),'..'),upstream=resolve(root,'upstream/anime');
import {timelineKeyframeCases as base} from './timeline-keyframe-cases.mjs';
const rename=value=>Array.isArray(value)?value.map(rename):value&&typeof value==='object'?Object.fromEntries(Object.entries(value).map(([name,value])=>[name==='x'?'width':name==='y'?'height':name,rename(value)])):value;
const cases=base.map(rename).flatMap(input=>['none','replace'].map(composition=>({...input,name:composition+'-'+input.name,composition})));
if(process.argv[2]==='--case'){
 let now=0;Date.now=()=>now;
 const load=path=>import(pathToFileURL(resolve(upstream,path)));
 const {createTimeline}=await load('src/timeline/timeline.js');
 const {JSAnimation}=await load('src/animation/animation.js');
 const {engine}=await load('src/engine/engine.js');
 const {tick}=await load('src/core/render.js');
 engine.useDefaultMainLoop=false;
 const input=cases[Number(process.argv[3])];assert.ok(input);
 const states=structuredClone(input.targets??[{width:10,height:20}]),writes=[],effects=[],events=[],trace=[],children=new Map();
 const record=(kind,value)=>{
  (kind==='write'?writes:kind==='effect'?effects:events).push(value);
  trace.push({kind,...value});
 };
 for(const state of states)for(const name of ['width','height'])state[name]=String(state[name])+'px';
 const targets=states.map((state,index)=>{
  const style={getPropertyValue:name=>state[name]??'',getPropertyPriority:()=>'',setProperty:(name,value)=>{style[name]=value;},
   removeProperty:name=>{const old=state[name]??'';state[name]='';record('write',{target:index,property:name,value:''});return old;}};
  for(const name of ['width','height'])Object.defineProperty(style,name,{enumerable:true,get:()=>state[name],
   set:value=>{state[name]=String(value);record('write',{target:index,property:name,value:String(value)});}});
  return {nodeType:1,style};
 });
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
   const targetValue=target.style.width;
   record('effect',{id,field:key,index,count:all.length,previous:previousValue,targetValue});
   const base=(value.base??0)+(value.step??0)*index;
   const result=value.fn==='target'?parseFloat(targetValue)+base:value.fn==='previous'?parseFloat(previousValue??targetValue)+base:base;
   return ['width','height','from','to'].includes(key)?String(result)+'px':result;
  };
  if(Array.isArray(value))return value.map(value=>convert(value,id,key));
  if(value&&typeof value==='object')return Object.fromEntries(Object.entries(value).map(([key,value])=>[key,convert(value,id,key)]));
  if(key==='modifier')return value=>{record('effect',{id,value});return input.children.find(child=>child.id===id).parameters.modifier==='plusOne'?value+1:value*2;};
  return (key==='width'||key==='height'||key==='to'||key==='from')&&typeof value==='number'?String(value)+'px':value;
 };
 timeline=createTimeline({...input.parameters,autoplay:false,...callbacks('root')});
 const tweens=child=>{
  const result=[];
  for(let tween=child._head;tween;tween=tween._next)result.push({target:targets.indexOf(tween.target),property:tween.property,
   start:number(tween._startTime),delay:number(tween._delay),updateDuration:number(tween._updateDuration),changeDuration:number(tween._changeDuration),
   absoluteStart:number(tween._absoluteStartTime),absoluteUpdateStart:number(tween._absoluteUpdateStartTime),absoluteEnd:number(tween._absoluteEndTime),
   from:number(tween._fromNumber),to:number(tween._toNumber),value:number(tween._value),hasFrom:!!tween._hasFromValue});
  return result;
 };
 const construction=[];
 for(const child of input.children){
  timeline.add(targets,{ease:'linear',composition:input.composition,...convert(child.parameters,child.id),...callbacks(child.id)},child.at);
  const controller=timeline._tail;
  assert.equal(controller.parent,timeline,'actual owned timeline child');
  assert.ok(controller instanceof JSAnimation,'actual animation child');
  for(let tween=controller._head;tween;tween=tween._next)assert.equal(tween._tweenType,2,'actual CSS property dispatch');
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
 assert.equal(byName.get('none-rawFrames').duration,225,'position contributes to parent duration');
 for(const fixture of fixtures)for(const stage of fixture.construction)assert.equal(stage.writes.length,0,'construction prepares caches without target writes');
 const muted=byName.get('none-mutedResetComplete');
 assert.equal(muted.samples[0].state.events.length,0,'muted owned child lifecycle is suppressed');
 const overlap=byName.get('replace-overlappingChildren').programs;
 assert.equal(overlap.a[0].changeDuration,75,'earlier tween is truncated at new child insertion');
 assert.equal(overlap.a[1].changeDuration,1e-11,'future earlier-child tween is overridden');
 assert.equal(overlap.b[0].from,75,'new child inherits the internally sampled predecessor');
 assert.equal(fixtures.length,42);
 assert.deepEqual(byName.get('replace-sparseRows').construction[0].snapshot.targets,[{width:'10px',height:'20px'},{width:'30px',height:'40px'}],'owned CSS construction leaves targets unchanged');
 assert.equal(git('status','--porcelain'),'');
 const text=JSON.stringify({upstreamCommit:revision,version:JSON.parse(readFileSync(resolve(upstream,'package.json'),'utf8')).version,
  scope:'Actual isolated Timeline.add animation children; normalized pre/post-overlap programs, construction/reset writes and input/modifier effects, owned child/root callbacks and self/target snapshots. Actual CSS property dispatch; native CSSOM/layout and independent-animation arbitration are separate qualifications.',fixtures},null,2)+'\n';
 writeFileSync(resolve(root,'tests/reference/timeline-css-keyframes.json'),text);
 console.log('Captured '+fixtures.length+' owned CSS timeline keyframe schedules; SHA256 '+createHash('sha256').update(text).digest('hex'));
}
