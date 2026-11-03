import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {readFileSync,writeFileSync} from 'node:fs';
import {resolve,dirname} from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
import {createHash} from 'node:crypto';
const script=fileURLToPath(import.meta.url);
const root=resolve(dirname(script),'..'),upstream=resolve(root,'upstream/anime');
const playback=process.argv.includes('--playback');
let cases=[
 {name:'oneValue',original:{x:10},duration:100,properties:{x:[100]}},
 {name:'fromTo',original:{x:10},duration:100,properties:{x:[20,100]}},
 {name:'threeValues',original:{x:10},duration:100,properties:{x:[0,100,50]}},
 {name:'fractionalSplit',original:{x:10},duration:4000,properties:{x:[100,200,300,400]}},
 {name:'emptyValues',original:{x:10},duration:100,properties:{x:[]}},
 {name:'objectSequence',original:{x:10},duration:100,properties:{x:[{to:100},{to:200}]}},
 {name:'explicitDurations',original:{x:10},duration:2000,properties:{x:[{to:-100,duration:800},{to:100},{to:50},{to:0,duration:1200}]}},
 {name:'firstDelay',original:{x:10},duration:100,delay:40,properties:{x:[{to:100},{to:200}]}},
 {name:'frameDelayOverrides',original:{x:10},duration:100,delay:40,properties:{x:[{to:100,delay:10},{to:200,delay:20}]}},
 {name:'negativeFrameDelay',original:{x:10},duration:100,delay:-20,properties:{x:[{to:100},{to:200,delay:-10}]}},
 {name:'holdFrame',original:{x:10},duration:300,properties:{x:[{to:100},{},{to:200}]}},
 {name:'fromOnly',original:{x:10},duration:100,properties:{x:[{from:20},{to:100}]}},
 {name:'relativeSequence',original:{x:10},duration:100,properties:{x:[{from:'+=5',to:'*=2'},{to:'-=3'},{from:'+=2',to:'+=10'}]}},
 {name:'unitInheritance',original:{x:'5px'},duration:100,properties:{x:[0,10,20]}},
 {name:'unitFrameInheritance',original:{x:'5px'},duration:100,properties:{x:[{from:10,to:20},{to:30},{from:40,to:50}]}},
 {name:'angleUnits',original:{x:'0deg'},duration:100,properties:{x:[{to:'1turn'},{to:'180deg'},{to:'3.141592653589793rad'}]}},
 {name:'colorSequence',original:{x:'rgba(1,2,3,0.5)'},duration:100,properties:{x:[{to:'rgba(10,20,30,0.25)'},{to:'#00ff00'},{to:'hsl(240,100%,50%)'}]}},
 {name:'compoundSequence',original:{x:'translate(10px,20px)'},duration:100,properties:{x:[{to:'translate(100px,200px,300px)'},{to:50},{to:'translate(20px,30px)'}]}},
 {name:'modifierRawChaining',original:{x:10},duration:100,modifier:'double',properties:{x:[{to:100},{to:200},{to:300}]}},
 {name:'frameModifier',original:{x:10},duration:100,modifier:'double',properties:{x:[{to:100},{to:200,modifier:'plusOne'},{to:300}]}},
 {name:'compoundModifier',original:{x:'v(10,20)'},duration:100,modifier:'double',properties:{x:[{to:'v(100,200)'},{to:'v(300,400)'}]}},
 {name:'colorModifierCache',original:{x:'#ff0000'},duration:100,modifier:'double',properties:{x:[{to:'#00ff00'},{to:'#0000ff'}]}},
 {name:'zeroFrame',original:{x:10},duration:100,properties:{x:[{to:100,duration:0},{to:200,duration:50}]}},
 {name:'fractionalFrames',original:{x:10},duration:0.125,delay:0.1,properties:{x:[{to:100},{to:200,delay:0.025},{to:300}]}},
 {name:'multipleProperties',original:{x:10,y:20},duration:300,delay:40,properties:{x:[{to:100},{to:200}],y:[{to:300,duration:100,delay:10},{to:400,duration:200,delay:20}]}},
 {name:'sparseRows',original:{x:10,y:20},duration:1500,delay:200,rows:[{y:-40},{x:250,duration:100,delay:300,ease:'inQuad'},{y:40},{x:0},{y:0}]},
 {name:'rowFromAndHolds',original:{x:10,y:20},duration:300,rows:[{x:100,from:50},{y:200},{x:0,y:0}]},
 {name:'offsets',original:{x:10,y:20},duration:1000,offsets:[{at:0,x:100,y:100},{at:0.2,x:-100},{at:0.5,x:100},{at:0.8,x:-100},{at:1,x:100,y:-100}]},
 {name:'fractionalOffsets',original:{x:10},duration:1000,offsets:[{at:0,x:0},{at:0.215,x:50},{at:1,x:100}]},
 {name:'offsetsWithoutZeroOrEnd',original:{x:10},duration:1000,offsets:[{at:0.2,x:20},{at:0.6,x:100}]},
 {name:'offsetSortAndEase',original:{x:10},duration:1000,ease:'outQuad',offsets:[{at:1,x:100},{at:0,x:0,ease:'inQuad'},{at:0.5,x:50,ease:'outQuad'}]},
 {name:'duplicateOffsets',original:{x:10},duration:1000,offsets:[{at:0,x:0},{at:0.2,x:20,key:'20%'},{at:0.2,x:40,key:'20.0%'},{at:1,x:100}]},
 {name:'zeroOffsetOnly',original:{x:10},duration:100,offsets:[{at:0,x:20}]},
 {name:'offsetsGlobalDelay',original:{x:10,y:20},duration:1000,delay:40,offsets:[{at:0,x:0},{at:0.5,x:50,y:100},{at:1,x:100,y:200}]},
 {name:'multipleTargets',targets:[{x:10},{x:50}],duration:100,properties:{x:[{to:100},{to:200}]}},
 {name:'explicitFramePairs',original:{x:10},duration:100,properties:{x:[{to:[20,40]},{to:[50,100]}]}},
 {name:'explicitColorsIgnoreOriginal',original:{x:'rgb(invalid)'},duration:100,properties:{x:[{from:'#ff0000',to:'#00ff00'}]}},
 {name:'explicitCompoundsIgnoreOriginal',original:{x:'rgb(invalid)'},duration:100,properties:{x:[{from:'translate(10px,20px)',to:'translate(100px,200px)'}]}},
 {name:'explicitUnitsIgnoreOriginal',original:{x:'rgb(invalid)'},duration:100,properties:{x:[{from:'10px',to:'100px'}]}},
 {name:'unitFramePairs',original:{x:'5px'},duration:100,properties:{x:[{to:[0,10]},{to:[20,'100px']}]}},
 ...[173.27,1000,5234.5,50000].map(offset=>({name:'absoluteOffset'+offset,offset,original:{x:10},duration:4000,delay:0.125,properties:{x:[100,200,300,400]}})),
];
if(playback){
 const base=cases;
 const edges=[
  {name:'gappedTail',original:{x:0},duration:300,properties:{x:[{to:100,duration:100},{to:200,duration:50,delay:100}]},times:[0,50,100,150,199,200,225,250,260,225,199,150,99,0,220,0]},
  {name:'overlappingFrames',original:{x:0},duration:300,properties:{x:[{to:100,duration:100},{to:200,duration:100,delay:-50},{to:300,duration:100,delay:-30}]}},
  {name:'reorderedFrames',original:{x:0},duration:300,properties:{x:[{to:100,duration:100},{to:200,duration:50,delay:-200},{from:'+=5',to:300,duration:100}]}},
  {name:'middleReorderRelative',original:{x:0},duration:300,properties:{x:[{to:100,duration:100},{to:200,duration:100},{from:'+=5',to:300,duration:100,delay:-150}]}},
  {name:'middleReorderUnit-angleUnits',original:{x:'0deg'},duration:300,properties:{x:[{to:'100deg',duration:100},{to:'1turn',duration:100},{from:1,to:'90deg',duration:100,delay:-150}]}},
  ...[false,true].flatMap(reversed=>[false,true].map(alternate=>({name:'loop-'+reversed+'-'+alternate,original:{x:0},duration:200,loop:2,loopDelay:70,reversed,alternate,properties:{x:[{to:100},{to:200,delay:30}]},times:[0,30,100,130,200,230,250,299,300,400,460,530,600,830,820,600,301,299,230,100,0]}))),
  {name:'stretchSequence',original:{x:'10px'},duration:200,delay:40,properties:{x:[{to:'100px'},{to:'200px',delay:30}]},actions:[['seek',50],['stretch',1000],['seek',50],['seek',500],['seek',1000],['seek',0],['stretch',500],['seek',250],['stretch',0],['seek',0],['seek',1]]},
  ...[0,40,-20].flatMap(delay=>[240,20].map(frameRate=>({name:'scheduled-'+delay+'-'+frameRate,original:{x:0,y:20},duration:300,delay,frameRate,loop:2,loopDelay:70,alternate:true,properties:{x:[{to:100,duration:100},{to:200,duration:100,delay:50}],y:[{to:100},{to:0}]},actions:[['resume'],['frame',1],['frame',25],['frame',25],['frame',100],['speed',2],['frame',50],['seek',200],['frame',50],['pause'],['frame',100],['reverse'],['frame',30],['play'],['frame',20],['alternate'],['frame',30],['reset'],['restart'],['frame',500],['seekMuted',50],['complete']]}))),
  ...[173.27,5234.5].map(offset=>({name:'stretchOffset-'+offset,offset,original:{x:0},duration:200,properties:{x:[{to:100},{to:200,delay:30}]},actions:[['seek',50],['stretch',1000],['seek',200],['seek',800],['seek',0],['stretch',500],['seek',400]]})),
 ];
 cases=[...base.map(input=>({...input,name:'none-'+input.name,composition:'none'})),
         ...[...base,...edges].map(input=>({...input,name:'replace-'+input.name,composition:'replace'}))];
}
if(process.argv[2]==='--case'){
 let now=0;
 Date.now=()=>now;
 const {JSAnimation}=await import(pathToFileURL(resolve(upstream,'src/animation/animation.js')));
 const {engine}=await import(pathToFileURL(resolve(upstream,'src/engine/engine.js')));
 const {tick}=await import(pathToFileURL(resolve(upstream,'src/core/render.js')));
 engine.useDefaultMainLoop=false;
 const input=cases[Number(process.argv[3])]; assert.ok(input);
 if(input.offset!==undefined)engine._startTime=engine._lastTickTime-input.offset;
 const targets=structuredClone(input.targets??[input.original]);
 const modifier=name=>name==='double'?value=>value*2:name==='plusOne'?value=>value+1:value=>value;
 const convert=value=>{
  if(Array.isArray(value))return value.map(convert);
  if(value&&typeof value==='object')return Object.fromEntries(Object.entries(value).map(([key,item])=>[key,key==='modifier'?modifier(item):convert(item)]));
  return value;
 };
 const writes=[],events=[];
 if(playback){
  const names=new Set([...Object.keys(input.properties??{}),...(input.rows??input.offsets??[]).flatMap(row=>Object.keys(row).filter(key=>!['at','key','duration','delay','ease','modifier'].includes(key)))]);
  for(const [index,target] of targets.entries())for(const name of names){
   let value=target[name];
   Object.defineProperty(target,name,{enumerable:true,configurable:true,get:()=>value,set:next=>{value=next;writes.push({target:index,property:name,value:next});}});
  }
 }
 const callbacks=playback?Object.fromEntries(['Begin','BeforeUpdate','Render','Update','Loop','Pause','Complete'].map(event=>['on'+event,animation=>events.push({event,time:animation.currentTime,targets:structuredClone(targets)})])):{};
 const parameters={duration:input.duration,delay:input.delay??0,ease:input.ease??'linear',modifier:modifier(input.modifier),composition:input.composition??'none',autoplay:false,loop:input.loop??0,loopDelay:input.loopDelay??0,reversed:input.reversed??false,alternate:input.alternate??false,frameRate:input.frameRate??240,...callbacks,...convert(input.properties??{})};
 if(input.rows)parameters.keyframes=convert(input.rows);
 if(input.offsets)parameters.keyframes=Object.fromEntries(input.offsets.map(({at,key,...values})=>[key??String(at*100)+'%',convert(values)]));
 const animation=new JSAnimation(targets,parameters);
 const cleanNumber=value=>Number.isFinite(value)?value:String(value);
 const clean=value=>Array.isArray(value)?value.map(clean):typeof value==='number'?cleanNumber(value):value;
 const tweens=[];
 for(let tween=animation._head;tween;tween=tween._next)tweens.push({
  target:targets.indexOf(tween.target),property:tween.property,start:cleanNumber(tween._startTime),delay:cleanNumber(tween._delay),
  absoluteStart:cleanNumber(tween._absoluteStartTime),absoluteUpdateStart:cleanNumber(tween._absoluteUpdateStartTime),absoluteEnd:cleanNumber(tween._absoluteEndTime),
  updateDuration:cleanNumber(tween._updateDuration),changeDuration:cleanNumber(tween._changeDuration),hasFrom:!!tween._hasFromValue,
  kind:tween._valueType,from:clean(tween._fromNumber),to:clean(tween._toNumber),unit:tween._unit,
  fromComponents:clean(tween._fromNumbers),toComponents:clean(tween._toNumbers),strings:tween._strings,
  cachedEnd:clean(tween._value),ease:[-0.25,0,0.25,0.5,1,1.25].map(time=>cleanNumber(tween._ease(time)))
 });
 const construction={delay:cleanNumber(animation._delay),iterationDuration:cleanNumber(animation.iterationDuration),duration:cleanNumber(animation.duration),tweens};
 animation.init();
 const capture=()=>({targets:structuredClone(targets),time:cleanNumber(animation.currentTime),iterationTime:cleanNumber(animation.iterationCurrentTime),paused:animation.paused,completed:animation.completed,backwards:animation.backwards,...(playback?{events:events.splice(0),duration:cleanNumber(animation.duration),iterationDuration:cleanNumber(animation.iterationDuration),writes:writes.splice(0),tweenTimes:(()=>{const values=[];for(let t=animation._head;t;t=t._next)values.push(cleanNumber(t._currentTime));return values;})()}: {})});
 const initial=capture();
 const end=animation.duration;
 const times=input.times??[-25,0,10,25,50,75,100,end/4,end/2,end,end+25,end/2,0];
 const samples=input.actions?input.actions.map(([kind,value])=>{
  if(kind==='frame'){
   now+=value;
   if(!animation.paused)tick(animation,(now-animation._startTime)*animation._speed,0,0,animation._fps<engine._fps?animation.requestTick(now):1);
  }else if(kind==='speed')animation.speed=value;
  else if(kind==='seekMuted')animation.seek(value,true);
  else animation[kind](value);
  return {action:kind,time:value??0,state:capture()};
 }):times.map(time=>{animation.seek(time);return {time,state:capture()};});
 animation.cancel();
 if(playback){now+=100;engine.update();}
 assert.equal(engine._head,null);
 process.stdout.write(JSON.stringify({name:input.name,input,construction,initial,samples}));
}else{
 const revision=execFileSync('git',['-C',upstream,'rev-parse','HEAD'],{encoding:'utf8'}).trim();
 const clean=()=>assert.equal(execFileSync('git',['-C',upstream,'status','--porcelain'],{encoding:'utf8'}).trim(),'');
 assert.equal(revision,'2c9cf8ea00329f6768c7d7902252ed977d75ce42');clean();
 const fixtures=cases.map((_,index)=>JSON.parse(execFileSync(process.execPath,[script,'--case',String(index),...(playback?['--playback']:[])],{encoding:'utf8',maxBuffer:8*1024*1024})));
 const byName=Object.fromEntries(fixtures.map(fixture=>[fixture.name,fixture]));
 if(!playback){
 assert.equal(byName.fromTo.construction.tweens.length,1);
 assert.equal(byName.threeValues.construction.tweens.length,2);
 assert.deepEqual(byName.explicitDurations.construction.tweens.map(tween=>tween.updateDuration),[800,500,500,1200]);
 assert.equal(byName.sparseRows.construction.tweens.length,10);
 assert.equal(byName.offsetsWithoutZeroOrEnd.construction.iterationDuration,600);
 assert.equal(byName.zeroOffsetOnly.construction.tweens.length,0);
 }
 clean();
 const output=JSON.stringify({upstreamCommit:revision,version:JSON.parse(readFileSync(resolve(upstream,'package.json'),'utf8')).version,
  scope:playback?'Actual isolated JSAnimation none and same-parent replace write-order/playhead traces; not cross-animation arbitration or native CSS acceptance.':'Actual unmodified JSAnimation construction and seek snapshots, isolated per case; composition none isolates keyframe normalization from global arbitration. These are object targets, not native CSS acceptance.',fixtures},null,2)+'\n';
 writeFileSync(resolve(root,'tests/reference/'+(playback?'keyframe-playback':'keyframes')+'.json'),output);
 console.log('Captured '+fixtures.length+' isolated keyframe traces; SHA256 '+createHash('sha256').update(output).digest('hex'));
}
