import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {readFileSync,writeFileSync} from 'node:fs';
import {resolve,dirname} from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
import {createHash} from 'node:crypto';
const script=fileURLToPath(import.meta.url),root=resolve(dirname(script),'..'),upstream=resolve(root,'upstream/anime');
const cases=[
 {name:'singleFrameRevert',originals:['0.25'],duration:100,frames:[{to:0.75}]},
 {name:'capturedAutoStartsAtZero',originals:['auto'],duration:100,frames:[{to:'100px'}]},
 {name:'numericCssPrecision',originals:['0.123456789'],duration:100,frames:[{to:1.234567891,duration:17.5},{to:2.345678912,duration:23.5}],modifier:'double'},
 {name:'unitGapsAndTargets',originals:['7px','25px'],duration:100,delay:20,frames:[{to:'100.123456px',duration:50.75},{to:'200.567891px',duration:20,delay:25}]},
 {name:'colors',originals:['#ff0000'],duration:200,frames:[{to:'#00ff00',duration:100},{to:'hsl(240,100%,50%)',duration:75,delay:25}]},
 {name:'previousColorCache',originals:['#ff0000'],duration:200,modifier:'double',frames:[{to:'#00ff00'},{to:{fn:'previous'}}]},
 {name:'compoundPaddingAndCache',originals:['translate(10px,20px)'],duration:200,modifier:'plusOne',frames:[{to:'translate(100px,200px,300px)'},{to:{fn:'previous'}}]},
 {name:'relativeUnits',originals:['7px'],duration:200,frames:[{from:'+=5px',to:'*=2'},{to:'+=100px'}]},
 {name:'zeroFrames',originals:['7px'],duration:100,frames:[{to:'100px',duration:0},{to:'200px',duration:50}]},
 {name:'repeatReverseAlternate',originals:['10px'],duration:200,loop:2,loopDelay:40,reversed:true,alternate:true,frames:[{to:'100px'},{to:'200px',delay:30}]},
 {name:'noneComposition',originals:['10px'],duration:200,composition:'none',frames:[{to:'100px'},{to:'200px',delay:30}]},
 {name:'typedTargetFunctions',originals:['10px','20px'],duration:200,frames:[{to:{fn:'shift',base:100,step:10,unit:'px'},duration:{fn:'index',base:100,step:50},delay:{fn:'index',base:20,step:5}},{to:{fn:'shiftPrevious',base:100,unit:'px'},duration:100}]},
 {name:'explicitFunctionPair',originals:['10px','20px'],duration:200,frames:[{to:{fn:'shift',base:100,unit:'px'},from:{fn:'index',base:5,step:5,unit:'px'},duration:100},{to:'200px'}]},
 {name:'emptyRepeated',originals:['10px'],duration:100,loop:3,frames:[]},
];
if(process.argv[2]==='--case'){
 Date.now=()=>0;
 const {JSAnimation}=await import(pathToFileURL(resolve(upstream,'src/animation/animation.js')));
 const {engine}=await import(pathToFileURL(resolve(upstream,'src/engine/engine.js')));
 engine.useDefaultMainLoop=false;
 const input=cases[Number(process.argv[3])];assert.ok(input);
 const property='opacity',values=[...input.originals],writes=[],calls=[],events=[];
 const targets=input.originals.map((_,index)=>{
  const style={getPropertyValue:name=>name===property?values[index]:'',getPropertyPriority:()=>'',setProperty:(name,value)=>{assert.equal(name,property);style[property]=value;},removeProperty:name=>{assert.equal(name,property);const old=values[index];values[index]='';writes.push({target:index,value:''});return old;}};
  Object.defineProperty(style,property,{enumerable:true,get:()=>values[index],set:value=>{values[index]=String(value);writes.push({target:index,value:String(value)});}});
  return {nodeType:1,style};
 });
 const modifier=name=>value=>{calls.push({field:'modifier',number:value});return name==='double'?value*2:name==='plusOne'?value+1:value;};
 const convert=(field,value)=>{
  if(value&&typeof value==='object'&&value.fn)return(target,index,all,previous)=>{
   const original=target.style[property],prior=previous?String(previous._value):null;
   calls.push({field,index,count:all.length,previous:prior,target:original});
   const base=(value.base??0)+(value.step??0)*index;
   if(value.fn==='previous')return previous?previous._value:original;
   const number=value.fn==='shift'?parseFloat(original)+base:value.fn==='shiftPrevious'?parseFloat(prior??original)+base:base;
   return value.unit===undefined?number:String(number)+value.unit;
  };
  return value;
 };
 const frames=input.frames.map(frame=>Object.fromEntries(Object.entries(frame).map(([field,value])=>[field,field==='modifier'?modifier(value):convert(field,value)])));
 const callback=event=>animation=>events.push({event,time:animation.currentTime,values:[...values]});
 const animation=new JSAnimation(targets,{[property]:frames,duration:input.duration,delay:input.delay??0,loop:input.loop??0,
  loopDelay:input.loopDelay??0,reversed:input.reversed??false,alternate:input.alternate??false,
  composition:input.composition??'replace',modifier:modifier(input.modifier),ease:'linear',autoplay:false,
  ...Object.fromEntries(['Begin','BeforeUpdate','Render','Update','Loop','Pause','Complete'].map(event=>['on'+event,callback(event)]))});
 for(let t=animation._head;t;t=t._next)assert.equal(t._tweenType,2,'actual CSS dispatch');
 const capture=()=>({values:[...values],writes:writes.splice(0),calls:calls.splice(0),events:events.splice(0),
  time:animation.currentTime,iterationTime:animation.iterationCurrentTime,duration:animation.duration,iterationDuration:animation.iterationDuration,
  backwards:animation.backwards,paused:animation.paused,completed:animation.completed});
 animation.init();
 const initial=capture(),end=animation.duration;
 const times=[-25,0,10,25,50,75,100,end/4,end/2,end,end+25,end/2,0];
 const samples=times.map(time=>{animation.seek(time);return {time,state:capture()};});
 animation.revert();
 const reverted=capture();
 animation.cancel();
 assert.equal(engine._head,null);
 process.stdout.write(JSON.stringify({name:input.name,input,initial,samples,reverted}));
}else{
 const git=(...args)=>execFileSync('rtk',['proxy','git','-C',upstream,...args],{encoding:'utf8'}).trim();
 const revision=git('rev-parse','HEAD');assert.equal(revision,'2c9cf8ea00329f6768c7d7902252ed977d75ce42');assert.equal(git('status','--porcelain'),'');
 const fixtures=cases.map((_,index)=>JSON.parse(execFileSync(process.execPath,[script,'--case',String(index)],{encoding:'utf8',maxBuffer:8*1024*1024})));
 assert.equal(git('status','--porcelain'),'');
 const output=JSON.stringify({upstreamCommit:revision,version:JSON.parse(readFileSync(resolve(upstream,'package.json'),'utf8')).version,
  scope:'Actual JSAnimation CSS dispatch asserted with recording-style targets. These traces verify dispatch/effect order and final revert values; they do not establish browser parsing, layout or native CSS acceptance.',fixtures},null,2)+'\n';
 writeFileSync(resolve(root,'tests/reference/keyframe-css.json'),output);
 console.log('Captured '+fixtures.length+' CSS keyframe schedules; SHA256 '+createHash('sha256').update(output).digest('hex'));
}
