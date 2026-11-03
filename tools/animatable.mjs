import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {readFileSync, writeFileSync} from 'node:fs';
import {resolve, dirname} from 'node:path';
import {fileURLToPath, pathToFileURL} from 'node:url';
import {createHash} from 'node:crypto';

const script = fileURLToPath(import.meta.url);
const root = resolve(dirname(script), '..');
const upstream = resolve(root, 'upstream/anime');
const retarget = (property, value, duration, ease) => ({op:'retarget', property, value, duration, ease});
const frame = after => ({op:'frame', after});
const seek = (property, time) => ({op:'seek', property, time});
const cases = [
  {name:'scalar', targets:[{x:10}], parameters:{x:100, ease:'linear'}, steps:[retarget('x',110),frame(25),frame(25),frame(100)]},
  {name:'defaultEase', targets:[{x:10}], parameters:{x:100}, steps:[retarget('x',110),frame(25),frame(25),frame(100)]},
  {name:'rapidRetarget', targets:[{x:0}], parameters:{x:100,ease:'linear'}, steps:[retarget('x',100),frame(25),retarget('x',200),retarget('x',300),frame(25),frame(100)]},
  {name:'externalEdit', targets:[{x:10}], parameters:{x:100,ease:'linear'}, steps:[retarget('x',110),frame(25),{op:'external',target:0,property:'x',value:900},retarget('x',210),frame(25),frame(100)]},
  {name:'modifierCached', targets:[{x:10}], parameters:{x:100,ease:'linear'}, modifier:'double', steps:[retarget('x',100),frame(25),retarget('x',200),frame(25),frame(100)]},
  {name:'propertyModifier', targets:[{x:5}], parameters:{x:{duration:100},ease:'linear'}, propertyModifier:'plusOne', steps:[retarget('x',20),frame(25),retarget('x',40),frame(25),frame(100)]},
  {name:'durationAndEaseRetained', targets:[{x:0}], parameters:{x:100,ease:'linear'}, steps:[retarget('x',100,200,'inQuad'),frame(50),retarget('x',200),frame(50),retarget('x',300,100,'linear'),frame(50),frame(100)]},
  {name:'fractionalDuration', targets:[{x:0}], parameters:{x:100,ease:'linear'}, steps:[retarget('x',100,0.125),frame(5),retarget('x',200),frame(5)]},
  {name:'zeroDuration', targets:[{x:10}], parameters:{x:100,ease:'linear'}, steps:[retarget('x',100,0),frame(5),retarget('x',200,100),frame(25),frame(100)]},
  {name:'uintDuration', targets:[{x:0}], parameters:{x:100,ease:'linear'}, steps:[retarget('x',100,4294967295),frame(2147483647),retarget('x',200),frame(2147483647)]},
  {name:'unitScalar', targets:[{x:'10px'}], parameters:{x:{duration:100,unit:'px'},ease:'linear'}, steps:[retarget('x',110),frame(25),retarget('x',210,200),frame(50),frame(200)]},
  {name:'unitFromProperty', targets:[{x:'10px'}], parameters:{x:100,ease:'linear'}, steps:[retarget('x',110),frame(25),frame(100)]},
  {name:'compoundPartial', targets:[{x:'translate(10px,20px)'}], parameters:{x:100,ease:'linear'}, steps:[retarget('x',[110,220]),frame(25),retarget('x',[210]),frame(25),retarget('x',[310,420,999]),frame(25),frame(100)]},
  {name:'compoundModifier', targets:[{x:'translate(10px,20px)'}], parameters:{x:100,ease:'linear'}, modifier:'double', steps:[retarget('x',[110,220]),frame(25),retarget('x',[210]),frame(25),frame(100)]},
  {name:'componentEmpty', targets:[{x:'translate(10px,20px)'}], parameters:{x:100,ease:'linear'}, steps:[retarget('x',[110,220]),frame(25),retarget('x',[]),frame(25),frame(100)]},
  {name:'componentAlias', targets:[{x:'translate(10px,20px)'}], parameters:{x:100,ease:'linear'}, steps:[{op:'mutateGetter',property:'x',index:0,value:500},retarget('x',[100,200]),frame(25),frame(100)]},
  {name:'color', targets:[{x:'rgba(0,40,80,0.5)'}], parameters:{x:100,ease:'linear'}, steps:[retarget('x',[255,100,20,1]),frame(25),retarget('x',[0]),frame(25),frame(100)]},
  {name:'hexColor', targets:[{x:'#ff000080'}], parameters:{x:100,ease:'linear'}, steps:[retarget('x',[0,255,0,0.25]),frame(25),frame(100)]},
  {name:'multipleTargets', targets:[{x:10},{x:50}], parameters:{x:100,ease:'linear'}, steps:[retarget('x',110),frame(25),retarget('x',210),frame(25),frame(100)]},
  {name:'multipleComponents', targets:[{x:'translate(10px,20px)'},{x:'translate(50px,60px)'}], parameters:{x:100,ease:'linear'}, steps:[retarget('x',[110,220]),frame(25),retarget('x',[210]),frame(25),frame(100)]},
  {name:'multipleProperties', targets:[{x:0,y:10}], parameters:{x:100,y:200,ease:'linear'}, steps:[retarget('x',100),retarget('y',210),frame(25),frame(75),frame(100),frame(10)]},
  {name:'longPropertyCallbacks', targets:[{x:0}], parameters:{x:2000,ease:'linear'}, steps:[retarget('x',100),frame(25),frame(500),frame(500),frame(500),frame(500)]},
  {name:'callbackRestart', targets:[{x:0,y:0}], parameters:{x:2000,y:100,ease:'linear'}, steps:[retarget('x',100),frame(25),frame(1100),retarget('y',100),frame(25),frame(100),frame(1000)]},
  {name:'globalDurationWins', targets:[{x:0}], parameters:{x:100,duration:300,ease:'linear'}, steps:[retarget('x',100),frame(100),frame(200)]},
  {name:'nestedDurationWins', targets:[{x:0}], parameters:{x:{duration:100},duration:300,ease:'linear'}, steps:[retarget('x',100),frame(25),frame(100)]},
  {name:'delayStretch', targets:[{x:0}], parameters:{x:{duration:100,delay:40},ease:'linear'}, steps:[retarget('x',100,200),frame(25),frame(50),frame(25),frame(200)]},
  {name:'loopStretch', targets:[{x:0}], parameters:{x:100,loop:2,loopDelay:25,alternate:true,ease:'linear'}, steps:[retarget('x',100,700),frame(25),frame(175),frame(100),retarget('x',200,350),frame(25),frame(350)]},
  {name:'reversed', targets:[{x:0}], parameters:{x:100,reversed:true,ease:'linear'}, steps:[retarget('x',100),frame(25),retarget('x',200),frame(25),frame(100)]},
  {name:'propertyControls', targets:[{x:10,y:20}], parameters:{x:100,y:200,ease:'linear'}, steps:[retarget('x',110),retarget('y',220),frame(25),{op:'pause',property:'x'},{op:'pause',property:'y'},frame(25),{op:'resume',property:'x'},frame(25),{op:'complete',property:'x'},frame(25)]},
  {name:'revert', targets:[{x:10,y:'translate(20px,30px)'}], parameters:{x:100,y:200,ease:'linear'}, steps:[retarget('x',110),retarget('y',[220,330]),frame(25),{op:'revert'},retarget('x',500),retarget('y',[500,600]),frame(100),{op:'revert'}]},
  {name:'noTargets', targets:[], parameters:{x:100}, steps:[{op:'get',property:'x'},retarget('x',100),frame(25),{op:'revert'}]},
  {name:'emptyProperties', targets:[{x:10}], parameters:{ease:'linear'}, steps:[frame(25),{op:'revert'}]},
  {name:'seekRetarget', targets:[{x:10}], parameters:{x:100,ease:'linear'}, steps:[retarget('x',110),seek('x',50),retarget('x',210),seek('x',50),seek('x',100)]},
];

if (process.argv[2] === '--case') {
  let clockNow = 0;
  Date.now = () => clockNow;
  const {createAnimatable} = await import(pathToFileURL(resolve(upstream,'src/animatable/animatable.js')));
  const {engine} = await import(pathToFileURL(resolve(upstream,'src/engine/engine.js')));
  engine.useDefaultMainLoop = false;
  const fixture = cases[Number(process.argv[3])];
  assert.ok(fixture, 'Known oracle case');
  const writes = [];
  const events = [];
  const targets = fixture.targets.map((values, index) => {
    const target = {};
    for (const [property, initial] of Object.entries(values)) {
      let value = initial;
      Object.defineProperty(target,property,{enumerable:true,configurable:true,get:()=>value,set:next=>{
        value = next;
        writes.push({target:index,property,value:next});
      }});
    }
    return target;
  });
  const targetValues = () => targets.map(target=>Object.fromEntries(Object.keys(target).map(key=>[key,target[key]])));
  const finite = value => Number.isFinite(value) ? value : String(value);
  const timerState = timer => ({time:finite(timer.currentTime),rawTime:finite(timer._currentTime),
    duration:finite(timer.duration),iterationDuration:finite(timer.iterationDuration),delay:finite(timer._delay),loopDelay:finite(timer._loopDelay),
    paused:timer.paused,completed:timer.completed,began:timer.began,reversed:timer.reversed});
  const callbacks = Object.fromEntries(['Begin','BeforeUpdate','Render','Update','Loop','Pause','Complete'].map(event=>
    ['on'+event,timer=>events.push({event,state:timerState(timer),targets:targetValues()})]));
  const modifier = name => name === 'double' ? value=>value*2 : value=>value+1;
  const parameters = structuredClone(fixture.parameters);
  if (fixture.modifier) parameters.modifier = modifier(fixture.modifier);
  if (fixture.propertyModifier) parameters.x.modifier = modifier(fixture.propertyModifier);
  const animatable = createAnimatable(targets,{...parameters,...callbacks});
  const capture = () => ({targets:targetValues(),targetCount:animatable.targets.length,
    properties:Object.fromEntries(Object.entries(animatable.animations).map(([name,animation])=>{
      const tweens = [];
      for (let tween=animation._head;tween;tween=tween._next) tweens.push({number:finite(tween._number),
        from:finite(tween._fromNumber),to:finite(tween._toNumber),components:tween._numbers ? [...tween._numbers] : null,
        fromComponents:tween._fromNumbers ? [...tween._fromNumbers] : null,toComponents:tween._toNumbers ? [...tween._toNumbers] : null});
      const value = animation._head ? animatable[name]() : undefined;
      return [name,{...timerState(animation),hasTween:!!animation._head,value:value === undefined ? null : Array.isArray(value) ? [...value] : value,tweens}];
    })),callbacks:animatable.callbacks ? timerState(animatable.callbacks) : null});
  const initial = {state:capture(),writes:[...writes],events:[...events]};
  const samples = fixture.steps.map(step=>{
    writes.length = 0;
    events.length = 0;
    let returnedGroup = null;
    let getterAliasesCache = null;
    if (step.op === 'retarget') returnedGroup = animatable[step.property](step.value,step.duration,step.ease) === animatable;
    else if (step.op === 'get') returnedGroup = animatable[step.property]() === animatable;
    else if (step.op === 'frame') { clockNow += step.after; engine.update(); }
    else if (step.op === 'external') targets[step.target][step.property] = step.value;
    else if (step.op === 'mutateGetter') {
      const value = animatable[step.property]();
      getterAliasesCache = value === animatable.animations[step.property]._head._numbers;
      value[step.index] = step.value;
    } else if (step.op === 'seek') animatable.animations[step.property].seek(step.time);
    else if (step.op === 'revert') returnedGroup = animatable.revert() === animatable;
    else animatable.animations[step.property][step.op]();
    return {step,clock:clockNow,returnedGroup,getterAliasesCache,state:capture(),writes:[...writes],events:[...events]};
  });
  animatable.revert();
  clockNow += 5;
  engine.update();
  assert.ok(engine._head === null,'All oracle animations detached after cleanup');
  process.stdout.write(JSON.stringify({...fixture,initial,samples}));
} else {
  const revision = execFileSync('git',['-C',upstream,'rev-parse','HEAD'],{encoding:'utf8'}).trim();
  const clean = () => assert.equal(execFileSync('git',['-C',upstream,'status','--porcelain'],{encoding:'utf8'}).trim(),'','Require clean pinned source');
  assert.equal(revision,'2c9cf8ea00329f6768c7d7902252ed977d75ce42');
  clean();
  const fixtures = cases.map((_,index)=>JSON.parse(execFileSync(process.execPath,[script,'--case',String(index)],{encoding:'utf8',maxBuffer:8*1024*1024})));
  const byName = Object.fromEntries(fixtures.map(fixture=>[fixture.name,fixture]));
  assert.equal(byName.scalar.initial.state.properties.x.value,10);
  assert.equal(byName.scalar.samples[0].writes.length,0,'Retarget soft reset must not write');
  assert.equal(byName.scalar.samples.at(-1).state.targets[0].x,110);
  assert.equal(byName.externalEdit.samples[2].state.properties.x.value,byName.externalEdit.samples[1].state.properties.x.value,'Getter uses cache after external edit');
  assert.equal(byName.componentAlias.samples[0].getterAliasesCache,true);
  assert.equal(byName.componentAlias.samples[0].state.properties.x.value[0],500);
  assert.equal(byName.globalDurationWins.initial.state.properties.x.duration,300);
  assert.equal(byName.nestedDurationWins.initial.state.properties.x.duration,100);
  assert.equal(byName.durationAndEaseRetained.samples[3].state.properties.x.duration,200);
  assert.equal(byName.revert.samples[3].state.targetCount,0);
  assert.deepEqual(byName.revert.samples[3].state.targets,cases.find(fixture=>fixture.name==='revert').targets);
  assert.equal(byName.revert.samples[4].returnedGroup,false,'Reverted source property becomes noop');
  const long = byName.longPropertyCallbacks.samples;
  assert.ok(long.some(sample=>sample.events.some(event=>event.event==='Complete') && !sample.state.properties.x.completed),'Callback clock can complete before property');
  clean();
  const text = JSON.stringify({upstreamCommit:revision,version:JSON.parse(readFileSync(resolve(upstream,'package.json'),'utf8')).version,
    clock:'Each case runs in a fresh process with Date.now starting at zero; frames call actual engine.update with its normal throttle and linked-list traversal.',
    scope:'Object targets only. Unit/color/compound values exercise source decomposition; these traces do not prove native DOM/CSS integration. Getter arrays are copied into snapshots after recording source aliasing explicitly.',fixtures},null,2)+'\n';
  writeFileSync(resolve(root,'tests/reference/animatable.json'),text);
  console.log(`Captured ${fixtures.length} isolated animatable traces; SHA256 ${createHash('sha256').update(text).digest('hex')}`);
}
