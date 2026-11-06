// Exercises Anime.js v4.5.0, copyright (c) 2025 Julian Garnier; see THIRD-PARTY-NOTICES.md.
import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {writeFileSync,readFileSync} from 'node:fs';
import {resolve,dirname} from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
const lane=dirname(fileURLToPath(import.meta.url));
const root=process.env.RANVIER_ANIME_ROOT??'C:/wk/ranvier-anime';
const upstream=resolve(root,'upstream/anime');
const git=(...args)=>execFileSync('rtk',['proxy','git','-C',upstream,...args],{encoding:'utf8'}).trim();
const revision=git('rev-parse','HEAD');
assert.equal(revision,'2c9cf8ea00329f6768c7d7902252ed977d75ce42');
assert.equal(git('status','--porcelain'),'');
Date.now=()=>0;
const {createLayout}=await import(pathToFileURL(resolve(upstream,'src/layout/layout.js')));
const {engine}=await import(pathToFileURL(resolve(upstream,'src/engine/engine.js')));
const {scope}=await import(pathToFileURL(resolve(upstream,'src/core/globals.js')));
engine.useDefaultMainLoop=false;
const raf=[];
let nativeRequests=[];
globalThis.requestAnimationFrame=fn=>(raf.push(fn),raf.length);
globalThis.Node={COMMENT_NODE:8,TEXT_NODE:3};
globalThis.window={scrollX:0,scrollY:0,scrollTo(x,y){this.scrollX=x;this.scrollY=y;}};
const camel=name=>name.replace(/-([a-z])/g,(_,c)=>c.toUpperCase());
class Style {
 constructor(){this.values=new Map();this.priority=new Map();}
 getPropertyValue(name){return this.values.get(camel(name))??'';}
 getPropertyPriority(name){return this.priority.get(camel(name))??'';}
 setProperty(name,value,priority=''){this.values.set(camel(name),String(value));this.priority.set(camel(name),priority);}
 removeProperty(name){let value=this.getPropertyValue(name);this.values.delete(camel(name));this.priority.delete(camel(name));return value;}
}
function style(){
 const target=new Style();
 return new Proxy(target,{
  has:()=>true,
  get(t,key){if(typeof key==='symbol'||key in t){const v=t[key];return typeof v==='function'?v.bind(t):v;} return t.getPropertyValue(key);},
  set(t,key,value){t.setProperty(key,value);return true;}
 });
}
const base={display:'block',visibility:'visible',opacity:'1',transform:'none',position:'static',fontSize:'16px',color:'rgb(0,0,0)',backgroundColor:'rgba(0,0,0,0)',borderRadius:'0px',border:'0px',filter:'none',clipPath:'none'};
class Element {
 constructor(name,rect=[0,0,100,100]){this.name=name;this.nodeType=1;this.dataset={};this.style=style();this.parentElement=null;this.children=[];this.rect=rect;this.clientLeft=0;this.clientTop=0;this.computed={};this.classes=new Set();this.classList={add:c=>this.classes.add(c),remove:c=>this.classes.delete(c),contains:c=>this.classes.has(c)};}
 append(child){if(child.parentElement)child.parentElement.children.splice(child.parentElement.children.indexOf(child),1);this.children.push(child);child.parentElement=this;return child;}
 contains(el){return this===el||this.children.some(c=>c.contains?.(el));}
 get lastElementChild(){return this.children.filter(c=>c.nodeType===1).at(-1)??null;}
 get previousElementSibling(){if(!this.parentElement)return null;let xs=this.parentElement.children.filter(c=>c.nodeType===1);return xs[xs.indexOf(this)-1]??null;}
 get previousSibling(){if(!this.parentElement)return null;return this.parentElement.children[this.parentElement.children.indexOf(this)-1]??null;}
 get nextSibling(){if(!this.parentElement)return null;return this.parentElement.children[this.parentElement.children.indexOf(this)+1]??null;}
 querySelectorAll(selector){return this.children.flatMap(c=>c.nodeType===1?[...(selector==='*'||selector===c.name?[c]:[]),...c.querySelectorAll(selector)]:[]);}
 getBoundingClientRect(){let [left,top,width,height]=this.rect;return {left,top,width,height};}
 getAttribute(name){return this[name]??null;}
 setAttribute(name,value){this[name]=String(value);}
 removeAttribute(name){if(name==='data-layout-id')delete this.dataset.layoutId;else delete this[name];}
 animate(keyframes,options){
  const request={target:this.name,keyframes,options,initialTransform:this.style.transform,initialTranslate:this.style.translate,times:[],operations:[]};
  nativeRequests.push(request);
  let time=0;
  return {playbackRate:1,get currentTime(){return time;},set currentTime(value){time=value;request.times.push(value);},
   play(){request.operations.push('play');},pause(){request.operations.push('pause');},reverse(){request.operations.push('reverse');},
   commitStyles(){request.operations.push('commitStyles');},cancel(){request.operations.push('cancel');}};
 }
}
globalThis.getComputedStyle=el=>new Proxy({...base,...el.computed},{get(t,key){if(key==='getPropertyValue')return n=>el.style.getPropertyValue(n)||t[camel(n)]||'';return el.style.getPropertyValue(key)||t[key]||'';}});
const nodeState=node=>({name:node.$el?.name??null,x:node.properties.x,y:node.properties.y,width:node.properties.width,height:node.properties.height,target:node.isTarget,hidden:node.measuredIsRemoved,inline:node.isInlined,sizeChanged:node.sizeChanged});
const state=layout=>({old:[...layout.oldState.nodes.values()].map(nodeState),fresh:[...layout.newState.nodes.values()].map(nodeState),entering:layout.entering.map(e=>e.name),leaving:layout.leaving.map(e=>e.name),animating:layout.animating.map(e=>e.name),swapping:layout.swapping.map(e=>e.name)});
const cases=[
 {name:'moveResize',mutate:({child})=>child.rect=[60,80,160,70]},
 {name:'borderLocalCoordinates',setup:({root})=>{root.clientLeft=3;root.clientTop=5;},mutate:({child})=>child.rect=[60,80,100,100]},
 {name:'fixedRoot',setup:({root})=>root.computed.position='fixed',mutate:({child})=>child.rect=[60,80,100,100]},
 {name:'inlineTextReflow',setup:({root,child})=>root.children.unshift({nodeType:3,textContent:'hello',parentElement:root}),mutate:({child})=>child.rect=[60,80,160,70]},
 {name:'hiddenExit',mutate:({child})=>child.computed.display='none'},
 {name:'hiddenEnter',setup:({child})=>child.computed.display='none',mutate:({child})=>child.computed.display='block'},
 {name:'newChild',mutate:({root})=>root.append(new Element('new',[40,50,30,20]))},
 {name:'removedChild',mutate:({root,child})=>{root.children.splice(root.children.indexOf(child),1);child.parentElement=null;}},
 {name:'parentReorder',setup:ctx=>ctx.parent=ctx.root.append(new Element('parent',[200,200,150,150])),mutate:({parent,child})=>{parent.append(child);child.rect=[220,230,100,100];}},
 {name:'onePixelTolerance',mutate:({child})=>child.rect=[30,40,101,99]},
 {name:'nonTargetSwap',children:'selected',setup:ctx=>{ctx.child.name='selected';ctx.child.append(new Element('unselected',[35,45,20,20]));},mutate:({child})=>{child.rect=[30,40,200,200];child.children[0].rect=[35,45,50,50];}},
 {name:'outsideVisibleTwin',children:ctx=>[ctx.child,ctx.outside],setup:ctx=>{ctx.child.dataset.layoutId='shared';ctx.child.computed.display='none';ctx.outside=new Element('outside',[100,120,100,100]);ctx.outside.dataset.layoutId='shared';},mutate:({outside})=>outside.rect=[160,180,160,70]},
 {name:'insideVisibleTwin',setup:ctx=>{ctx.child.dataset.layoutId='shared';ctx.child.computed.display='none';ctx.twin=ctx.root.append(new Element('twin',[100,120,100,100]));ctx.twin.dataset.layoutId='shared';},mutate:({twin})=>twin.rect=[160,180,160,70]},
 {name:'visibilityHiddenExit',mutate:({child})=>child.computed.visibility='hidden'},
 {name:'nestedHiddenExit',setup:({child})=>child.append(new Element('nested',[35,45,20,20])),mutate:({child})=>child.computed.display='none'},
 {name:'gridReflow',setup:({child})=>child.computed.display='grid',mutate:({child})=>child.rect=[60,80,160,70]},
 {name:'nativeTransformRequest',setup:({child})=>child.computed.transform='matrix(1,0,0,1,10,0)',mutate:({child})=>child.computed.transform='matrix(1,0,0,1,110,0)'},
 {name:'nativeTransformOvershootRequest',ease:t=>t===0?0:1.25,setup:({child})=>child.computed.transform='matrix(1,0,0,1,10,0)',mutate:({child})=>child.computed.transform='matrix(1,0,0,1,110,0)'},
 {name:'entryFunctionValuesAndTiming',params:{enterFrom:{opacity:(_,index,targets)=>0.2+index/targets.length,duration:200,delay:20,ease:'linear'}},setup:({child})=>child.computed.display='none',mutate:({child})=>child.computed.display='block'},
 {name:'layoutPlaybackEase',params:{playbackEase:time=>time*time},mutate:({child})=>child.rect=[60,80,160,70]},
 {name:'layoutAlternateRepeat',params:{loop:1,alternate:true},mutate:({child})=>child.rect=[60,80,160,70]},
 {name:'nonTargetSwapPlaybackEase',children:'selected',unmuted:true,params:{playbackEase:time=>time*time},setup:ctx=>{ctx.child.name='selected';ctx.child.append(new Element('unselected',[35,45,20,20]));},mutate:({child})=>{child.rect=[30,40,200,200];child.children[0].rect=[35,45,50,50];}}
];
const fixtures=[];
for(const input of cases){
 const rootEl=new Element('root',[10,20,300,300]),child=rootEl.append(new Element('child',[30,40,100,100]));
 const context={root:rootEl,child};
 nativeRequests=[];
 input.setup?.(context);scope.root=rootEl;
 const events=[];
 const callback=event=>timeline=>events.push({event,time:timeline.currentTime,active:rootEl.classList.contains('is-animated')});
 const children=typeof input.children==='function'?input.children(context):input.children??'*';
 const layout=createLayout(rootEl,{duration:100,ease:input.ease??'linear',autoplay:false,children,...input.params,
  ...Object.fromEntries(['Begin','BeforeUpdate','Render','Update','Loop','Pause','Complete'].map(event=>['on'+event,callback(event)]))});
 const initial=state(layout);
 input.mutate(context);
 const timeline=layout.animate();
 const captured=state(layout);
 const samples=[];
 for(const time of [...new Set([0,25,49,50,51,75,99,timeline.duration/2,timeline.duration*0.75,timeline.duration-1])]){
  timeline.seek(time,!input.unmuted);
  samples.push({time,values:[rootEl,...rootEl.querySelectorAll('*')].map(e=>({name:e.name,translate:e.style.translate,width:e.style.width,height:e.style.height,opacity:e.style.opacity,position:e.style.position}))});
 }
 timeline.resume();timeline.pause();
 const pause={events:events.splice(0),active:rootEl.classList.contains('is-animated'),state:state(layout)};
 layout.revert();
 while(raf.length)raf.shift()(0);
 const reverted=[rootEl,...rootEl.querySelectorAll('*')].map(e=>({name:e.name,inline:[...e.style.values],priority:[...e.style.priority]}));
 const describe=value=>typeof value==='function'?{function:String(value)}:value instanceof Element?{target:value.name}:Array.isArray(value)?value.map(describe):value&&typeof value==='object'?Object.fromEntries(Object.entries(value).map(([key,value])=>[key,describe(value)])):value;
 fixtures.push({name:input.name,input:describe({duration:100,ease:input.ease??'linear',children,...input.params,setup:input.setup,mutation:input.mutate}),
  initial,captured,duration:timeline.duration,samples,pause,reverted,nativeRequests,events:events.splice(0)});
 timeline.cancel();
}
assert.equal(git('status','--porcelain'),'');
const result={upstreamCommit:revision,version:JSON.parse(readFileSync(resolve(upstream,'package.json'))).version,scope:'Executes untouched AutoLayout and actual JS Timeline against deterministic recording HTML-shaped objects. Rectangles and computed CSS are inputs. Native requests record actual WAAPI keyframes/timing/control calls without simulating native interpolation. This proves source calculations/classification/playback/request shape only; it does not prove real CSS parsing, WebCore layout, rendering or acceleration.',fixtures};
writeFileSync(resolve(lane,'../tests/reference/layout.json'),JSON.stringify(result,null,2)+'\n');
console.log('Captured '+fixtures.length+' actual pinned AutoLayout traces.');

