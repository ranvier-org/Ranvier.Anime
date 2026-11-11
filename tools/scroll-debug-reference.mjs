// Anime.js v4.5.0, MIT, Copyright (c) 2025 Julian Garnier.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
const sourceRoot=process.env.RANVIER_ANIME_ROOT;
if(!sourceRoot)throw new Error('RANVIER_ANIME_ROOT is required');
class FakeElement {
  constructor(){this.nodeType=1;this.children=[];this.parentNode=null;this.style={position:'relative'};this.innerHTML='';this.classList={add:name=>this.className=name};}
  appendChild(node){this.children.push(node);node.parentNode=this;return node;}
  removeChild(node){this.children.splice(this.children.indexOf(node),1);node.parentNode=null;return node;}
  querySelector(){return this.children.find(node=>node.className==='animejs-onscroll-debug')??null;}
  getAttribute(){return null;}
}
globalThis.NodeList=class {};
globalThis.HTMLCollection=class {};
globalThis.SVGElement=class {};
globalThis.requestAnimationFrame=()=>0;
globalThis.cancelAnimationFrame=()=>{};
globalThis.window={requestAnimationFrame:globalThis.requestAnimationFrame,cancelAnimationFrame:globalThis.cancelAnimationFrame};
globalThis.document={body:new FakeElement(),createElement:()=>new FakeElement(),addEventListener(){}};
globalThis.getComputedStyle=element=>({getPropertyValue:name=>element.style[name]??''});
window.getComputedStyle=globalThis.getComputedStyle;
const {ScrollObserver}=await import(pathToFileURL(path.join(sourceRoot,'upstream/anime/src/events/scroll.js')));
const cases=[];
for(const horizontal of [false,true])for(const width of [320,600])for(const offset of [0,800,2300]){
  const element=new FakeElement();
  const observer=Object.create(ScrollObserver.prototype);
  Object.assign(observer,{container:{element,width,height:400,scrollWidth:2400,scrollHeight:2400,zIndex:0,useWin:false},horizontal,index:0,id:'probe',offset,coords:[0,100,600,0],thresholds:['start','end','end','start'],debugStyles:null,$debug:null});
  observer.debug();
  const overlay=observer.$debug;
  const labels=overlay.children.flatMap((part,partIndex)=>part.children.map(node=>({view:partIndex===0,text:node.children[0].innerHTML,css:node.style.cssText,position:node.style[horizontal?'left':'top']})));
  cases.push({horizontal,width,offset,overlayCSS:overlay.style.cssText,labels});
  observer.removeDebug();
  if(element.children.length!==0)throw new Error('debug DOM leak');
}
const output=path.join(path.dirname(fileURLToPath(import.meta.url)),'..','tests','reference','scroll-debug-reference.json');
fs.writeFileSync(output,JSON.stringify({baselineCommit:'2c9cf8ea00329f6768c7d7902252ed977d75ce42',harness:'Unmodified ScrollObserver.prototype.debug/removeDebug with explicit DOM structure capture; native CSS parsing and position-set animation excluded by preexisting relative container.',cases},null,2)+'\n');
console.log('scroll debug oracle: '+cases.length+' cases -> '+output);
