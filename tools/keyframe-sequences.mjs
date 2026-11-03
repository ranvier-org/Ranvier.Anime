import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {readFileSync,writeFileSync} from 'node:fs';
import {resolve,dirname} from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
import {createHash} from 'node:crypto';

const script=fileURLToPath(import.meta.url),root=resolve(dirname(script),'..'),upstream=resolve(root,'upstream/anime');
const base=[
 {name:'sparseTargetTiming',targets:[{x:10,y:20},{x:30,y:40}],duration:300,rows:[
  {x:{fn:'index',base:100,step:20},duration:{fn:'index',base:100,step:25},delay:{fn:'index',base:20,step:5}},
  {y:{fn:'previous',base:200,step:10},duration:100,modifier:'plusOne'},{x:0,y:0,duration:100}]},
 {name:'rowTimingOrderMutation',original:{x:10,y:20},duration:200,rows:[
  {x:{fn:'mutate',base:40},y:{fn:'index',base:200},duration:{fn:'index',base:100},delay:{fn:'index',base:0}},
  {x:{fn:'previous',base:100},y:{fn:'previous',base:200}}]},
 {name:'offsetZeroProviders',targets:[{x:10,y:20},{x:30,y:40}],duration:200,offsets:[
  {at:0,x:{fn:'index',base:0,step:5},y:{fn:'index',base:10,step:5},ease:'inQuad'},
  {at:0.5,x:{fn:'previous',base:100,step:10},ease:'outQuad'},
  {at:1,x:{fn:'previous',base:100},y:{fn:'previous',base:200}}]},
 {name:'offsetNonzeroProviderReevaluation',original:{x:10,y:20},duration:200,offsets:[
  {at:0.25,x:{fn:'target',base:20}},{at:0.75,x:{fn:'mutate',base:80}}]},
 {name:'sparseDuplicateOffsets',targets:[{x:10,y:20},{x:30,y:40}],duration:200,delay:40,loop:1,alternate:true,offsets:[
  {at:0,x:0,y:0},{at:0.2,key:'20%',x:{fn:'index',base:20,step:5}},
  {at:0.2,key:'20.0%',x:{fn:'previous',base:40}},{at:1,x:100,y:{fn:'index',base:200,step:25}}]},
 {name:'emptyOffsetProviders',original:{x:10,y:20},duration:100,loop:3,offsets:[{at:0,x:{fn:'mutate',base:400}}]},
 {name:'signedRowReordering',original:{x:0,y:10},duration:300,rows:[
  {x:100,y:200,duration:100},{x:{fn:'previous',base:100},y:200,duration:100,delay:-150},{y:0,duration:100}]},
 {name:'dynamicRowsOverrideSubprecisionDefaults',original:{x:10,y:20},duration:1e-12,rows:[
  {x:{fn:'index',base:100},duration:{fn:'index',base:100}},
  {y:{fn:'index',base:200},duration:{fn:'index',base:100}},
  {x:{fn:'index',base:300},duration:{fn:'index',base:100}}]},
];
const rename=value=>Array.isArray(value)?value.map(rename):value&&typeof value==='object'?Object.fromEntries(Object.entries(value).map(([name,value])=>[name==='x'?'width':name==='y'?'height':name,rename(value)])):value;
const cases=['numeric','css'].flatMap(mode=>['none','replace'].flatMap(composition=>base.map(value=>{
 const input=mode==='css'?rename(value):structuredClone(value);
 return {...input,name:mode+'-'+composition+'-'+input.name,mode,composition};
})));
if(process.argv[2]==='--case'){
 Date.now=()=>0;
 const {JSAnimation}=await import(pathToFileURL(resolve(upstream,'src/animation/animation.js')));
 const {engine}=await import(pathToFileURL(resolve(upstream,'src/engine/engine.js')));
 engine.useDefaultMainLoop=false;
 const input=cases[Number(process.argv[3])];assert.ok(input);
 const css=input.mode==='css',defaultName=css?'width':'x',writes=[],calls=[],events=[];
 const reserved=new Set(['at','key','duration','delay','ease','modifier']);
 const names=[...new Set((input.rows??input.offsets).flatMap(row=>Object.keys(row).filter(name=>!reserved.has(name))))];
 const states=structuredClone(input.targets??[input.original]);
 if(css)for(const state of states)for(const name in state)state[name]=String(state[name])+'px';
 const targets=states.map((state,index)=>{
  if(!css){
   for(const name of names){
    let value=state[name];
    Object.defineProperty(state,name,{enumerable:true,get:()=>value,set:next=>{value=next;writes.push({target:index,property:name,value:next});}});
   }
   return state;
  }
  const style={getPropertyValue:name=>state[name]??'',getPropertyPriority:()=>'',setProperty:(name,value)=>{style[name]=value;},removeProperty:name=>{const old=state[name]??'';state[name]='';writes.push({target:index,property:name,value:''});return old;}};
  for(const name of names)Object.defineProperty(style,name,{enumerable:true,get:()=>state[name]??'',set:value=>{state[name]=String(value);writes.push({target:index,property:name,value:String(value)});}});
  return {nodeType:1,style};
 });
 const snapshot=()=>structuredClone(states);
 const modifier=name=>value=>{calls.push({field:'modifier',value});return name==='plusOne'?value+1:name==='double'?value*2:value;};
 const convert=(value,field)=>{
  if(value&&typeof value==='object'&&value.fn)return(target,index,all,previous)=>{
   const state=states[index],targetValue=state[defaultName],prior=previous?previous._value:null;
   calls.push({field,index,count:all.length,previous:prior,targetValue});
   const base=(value.base??0)+(value.step??0)*index;
   if(value.fn==='mutate'){
    if(css)target.style[defaultName]=String(base)+'px';else target[defaultName]=base;
   }
   const result=value.fn==='target'?parseFloat(targetValue)+base:value.fn==='previous'?parseFloat(prior??targetValue)+base:base;
   return css&&names.includes(field)?String(result)+'px':result;
  };
  if(Array.isArray(value))return value.map(value=>convert(value,field));
  if(value&&typeof value==='object')return Object.fromEntries(Object.entries(value).map(([name,value])=>[name,name==='modifier'?modifier(value):convert(value,name)]));
  return css&&names.includes(field)&&typeof value==='number'?String(value)+'px':value;
 };
 const callback=phase=>animation=>events.push({event:phase,time:animation.currentTime,targets:snapshot()});
 const parameters={duration:input.duration,delay:input.delay??0,ease:'linear',modifier:modifier(),composition:input.composition,
  autoplay:false,loop:input.loop??0,loopDelay:input.loopDelay??0,reversed:input.reversed??false,alternate:input.alternate??false,
  ...Object.fromEntries(['Begin','BeforeUpdate','Render','Update','Loop','Pause','Complete'].map(phase=>['on'+phase,callback(phase)]))};
 parameters.keyframes=input.rows?convert(input.rows):Object.fromEntries(input.offsets.map(({at,key,...values})=>[key??String(at*100)+'%',convert(values)]));
 const animation=new JSAnimation(targets,parameters);
 if(css)for(let tween=animation._head;tween;tween=tween._next)assert.equal(tween._tweenType,2,'actual CSS dispatch');
 animation.init();
 const capture=()=>({targets:snapshot(),writes:writes.splice(0),calls:calls.splice(0),events:events.splice(0),
  time:animation.currentTime,iterationTime:animation.iterationCurrentTime,duration:animation.duration,iterationDuration:animation.iterationDuration,
  paused:animation.paused,completed:animation.completed,backwards:animation.backwards});
 const initial=capture(),end=animation.duration;
 const samples=[-25,0,10,25,50,75,100,end/4,end/2,end,end+25,end/2,0].map(time=>{animation.seek(time);return {time,state:capture()};});
 animation.cancel();
 assert.equal(engine._head,null);
 process.stdout.write(JSON.stringify({name:input.name,input,initial,samples}));
}else{
 const git=(...args)=>execFileSync('rtk',['proxy','git','-C',upstream,...args],{encoding:'utf8'}).trim();
 const commit=git('rev-parse','HEAD');assert.equal(commit,'2c9cf8ea00329f6768c7d7902252ed977d75ce42');assert.equal(git('status','--porcelain'),'');
 const fixtures=cases.map((_,index)=>JSON.parse(execFileSync(process.execPath,[script,'--case',String(index)],{encoding:'utf8',maxBuffer:8*1024*1024})));
 assert.equal(git('status','--porcelain'),'');
 const output=JSON.stringify({upstreamCommit:commit,version:JSON.parse(readFileSync(resolve(upstream,'package.json'),'utf8')).version,
  scope:'Actual isolated numeric and CSS JSAnimation sparse-row/offset dispatch, input/modifier effect order, callbacks and clock state. CSS recording styles prove dispatch/interpolation, not browser parsing or native layout. Native snapshots intentionally use pre-construction originals.',fixtures},null,2)+'\n';
 writeFileSync(resolve(root,'tests/reference/keyframe-sequences.json'),output);
 console.log('Captured '+fixtures.length+' numeric/CSS sparse sequence schedules; SHA256 '+createHash('sha256').update(output).digest('hex'));
}
