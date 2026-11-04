// Anime.js MIT, Copyright (c) 2025 Julian Garnier; see canonical THIRD-PARTY-NOTICES.md.
import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {writeFileSync} from 'node:fs';
import {resolve,dirname} from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
const lane=dirname(fileURLToPath(import.meta.url)),script=fileURLToPath(import.meta.url);
const root=process.env.RANVIER_ANIME_ROOT;assert.ok(root);
const upstream=resolve(root,'upstream/anime');
const git=(...args)=>execFileSync('rtk',['proxy','git','-C',upstream,...args],{encoding:'utf8'}).trim();
const frame=after=>({op:'frame',after}),move=(x,y)=>({op:'move',x,y});
const cases=[
 {name:'scalarLengthUnits',initial:[0,0],initialText:'translate(3em,4%)'},
 {name:'scalarAngleUnit',initial:[0,0],initialText:'rotate(0.5turn)',mapX:'rotate'},
 {name:'singleNumberCalc',initial:[0,0],initialText:'translateX(calc(10px))'},
 {name:'multipleNumberCalc',initial:[0,0],initialText:'translateX(calc(10px + 2%))'},
 {name:'variableFallback',initial:[0,0],initialText:'translateX(var(--n, 2px))'},
 {name:'multipleNumberSkew',initial:[0,0],initialText:'skew(4deg,5deg)',mapX:'skew'},
 {name:'scalarScale',initial:[0,0],initialText:'scale(2)',mapX:'scale'}
].map(fixture=>({...fixture,bounds:[-500,500,500,-500],steps:[{op:'set',x:12,y:7},frame(17),{op:'down'},move(20,10),frame(17),{op:'up'},frame(85),frame(85)]}));
if(process.argv[2]==='--case'){
 let clock=0;Date.now=()=>clock;
 const property=name=>name.replace(/[A-Z]/g,value=>'-'+value.toLowerCase());
 const style=()=>{
  const values=new Map([['position','relative'],['overflow','visible'],['z-index','0']]);
  const methods={getPropertyValue:name=>values.get(property(name))||'',getPropertyPriority:()=>'',
   setProperty:(name,value)=>values.set(property(name),String(value)),removeProperty:name=>{const old=values.get(property(name))||'';values.delete(property(name));return old},item:index=>[...values.keys()][index]||''};
  return new Proxy(methods,{has:()=>true,get:(target,name)=>name==='length'?values.size:name==='cssText'?[...values].map(([k,v])=>k+':'+v).join(';'):name in target?target[name]:values.get(property(String(name)))||'',set:(target,name,value)=>{values.set(property(String(name)),String(value));return true}});
 };
 class ElementStub{
  constructor(width=10,height=10){this.nodeType=1;this.style=style();this.parentElement=null;this.width=width;this.height=height;this.scrollLeft=0;this.scrollTop=0;this.scrollWidth=width;this.scrollHeight=height;this.clientWidth=width;this.clientHeight=height;this.classList={add(){},remove(){}};this.attributes=new Map();}
  addEventListener(){}removeEventListener(){}getAttribute(name){return this.attributes.get(name)||null}setAttribute(name,value){this.attributes.set(name,value)}hasAttribute(name){return this.attributes.has(name)}removeAttribute(name){this.attributes.delete(name)}
  getBoundingClientRect(){return{top:0,left:0,right:this.width,bottom:this.height,width:this.width,height:this.height}}
  scrollTo(x,y){this.scrollLeft=x;this.scrollTop=y}scrollBy(x,y){this.scrollLeft+=x;this.scrollTop+=y}
 }
 globalThis.Element=ElementStub;globalThis.HTMLElement=ElementStub;globalThis.SVGElement=class {};
 globalThis.NodeList=class extends Array{};globalThis.HTMLCollection=class extends Array{};
 globalThis.DOMPoint=class{constructor(){this.x=0;this.y=0}matrixTransform(){return{x:this.x,y:this.y}}};
 globalThis.DOMMatrix=class{inverse(){return this}preMultiplySelf(){return this}};
 globalThis.ResizeObserver=class{constructor(callback){this.callback=callback}observe(){}disconnect(){}};
 const body=new ElementStub(1000,1000);
 const doc={body,hidden:false,addEventListener(){},removeEventListener(){},querySelectorAll(){return[]}};
 globalThis.document=doc;
 globalThis.window={document:doc,innerWidth:1000,innerHeight:1000,scrollX:0,scrollY:0,addEventListener(){},removeEventListener(){},scrollTo(){},scrollBy(){}};
 globalThis.getComputedStyle=element=>element.style;
 globalThis.matchMedia=()=>({matches:true,addEventListener(){},removeEventListener(){}});
 globalThis.requestAnimationFrame=()=>1;globalThis.cancelAnimationFrame=()=>{};
 const {engine}=await import(pathToFileURL(resolve(upstream,'src/engine/engine.js')));engine.useDefaultMainLoop=false;
 const {createDraggable}=await import(pathToFileURL(resolve(upstream,'src/draggable/draggable.js')));
 const {spring}=await import(pathToFileURL(resolve(upstream,'src/easings/spring/index.js')));
 const fixture=cases[Number(process.argv[3])];assert.ok(fixture);
 const target=new ElementStub();target.parentElement=body;target.ownerDocument=doc;
 target.style.transform=fixture.initialText;
 let draggable,settleReset=false;const events=[];
 const capture=()=>({inline:target.style.transform,xFinite:Number.isFinite(draggable.x),yFinite:Number.isFinite(draggable.y),x:draggable.x,y:draggable.y,destX:draggable.destX,destY:draggable.destY,deltaX:draggable.deltaX,deltaY:draggable.deltaY,velocity:draggable.velocity,angle:draggable.angle,grabbed:draggable.grabbed,dragged:draggable.dragged,released:draggable.released,manual:draggable.manual});
 const callbacks=Object.fromEntries(['Grab','Drag','Release','Update','Settle','Snap'].map(event=>['on'+event,self=>{
  events.push({event,state:{x:self.x,y:self.y,velocity:self.velocity,grabbed:self.grabbed,dragged:self.dragged,released:self.released}});
  if(event==='Release'&&fixture.callback==='releaseStop')self.stop();
  if(event==='Settle'&&fixture.callback==='settleReset'&&!settleReset){settleReset=true;self.reset()}
 }]));
 const modifier=name=>name==='double'?value=>value*2:value=>value+1;
 draggable=createDraggable(target,{container:fixture.bounds,x:fixture.mapX?{mapTo:fixture.mapX}:fixture.modifierX?{modifier:modifier(fixture.modifierX)}:true,
  y:fixture.disableY?false:fixture.alias?{mapTo:'translateX'}:fixture.modifierY?{modifier:modifier(fixture.modifierY)}:true,
  minVelocity:fixture.minVelocity,maxVelocity:fixture.maxVelocity,snap:fixture.snap,...(fixture.globalModifier?{modifier:modifier(fixture.globalModifier)}:{}),...(fixture.spring?{releaseEase:spring({mass:1,stiffness:80,damping:20})}:{}),...callbacks});
 const initial=capture();events.length=0;
 const event=(x=0,y=0)=>({target,clientX:x,clientY:y,cancelable:true,preventDefault(){},stopPropagation(){}});
 const samples=fixture.steps.map(step=>{
  events.length=0;
  if(step.op==='set'){draggable.x=step.x;draggable.y=step.y}
  else if(step.op==='down')draggable.handleDown(event());
  else if(step.op==='move')draggable.handleMove(event(step.x,step.y));
  else if(step.op==='up')draggable.handleUp();
  else if(step.op==='disable')draggable.disable();
  else if(step.op==='enable')draggable.enable();
  else{clock+=step.after;engine.update()}
  return{step,clock,state:capture(),events:[...events]};
 });
 draggable.revert();clock+=17;engine.update();
 process.stdout.write(JSON.stringify({...fixture,initialCoordinates:fixture.initial,initial,samples}));
}else{
 assert.equal(git('rev-parse','HEAD'),'2c9cf8ea00329f6768c7d7902252ed977d75ce42');assert.equal(git('status','--porcelain'),'');
 const fixtures=cases.map((_,index)=>JSON.parse(execFileSync('rtk',['proxy',process.execPath,script,'--case',String(index)],{encoding:'utf8',maxBuffer:8*1024*1024})));
 const byName=Object.fromEntries(fixtures.map(fixture=>[fixture.name,fixture]));
 for(const name of ['multipleNumberCalc','multipleNumberSkew']){
  assert.equal(byName[name].initial.xFinite,false);
  assert.equal(byName[name].samples[1].state.xFinite,false);
 }
 for(const name of ['singleNumberCalc','variableFallback']){
  assert.equal(byName[name].initial.x,0);
  assert.equal(byName[name].samples[1].state.x,0);
 }
 assert.equal(byName.scalarLengthUnits.samples[1].state.inline,'translate(12em,7%) ');
 assert.equal(byName.scalarAngleUnit.samples[1].state.inline,'translateY(7px) rotate(12turn) ');
 assert.equal(byName.scalarScale.samples[1].state.inline,'translateY(7px) scale(12) ');
 assert.equal(git('status','--porcelain'),'');
 writeFileSync(resolve(lane,'inline-reference.json'),JSON.stringify({upstreamCommit:git('rev-parse','HEAD'),scope:'Actual unmodified Draggable constructor, methods, Timer, Animatable, Spring and engine. Fixed synthetic DOM/CSS and identity ancestor geometry isolate pixel physics/callbacks; no native DOM proof.',fixtures},null,2)+'\n');
 console.log(`Captured ${fixtures.length} source inline scalar/complex behavior traces`);
}
