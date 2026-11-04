// Anime.js v4.5.0, MIT, Copyright (c) 2025 Julian Garnier.
import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
const root=process.env.RANVIER_ANIME_ROOT;
if(!root)throw new Error('RANVIER_ANIME_ROOT is required');
globalThis.window={requestAnimationFrame:()=>0,cancelAnimationFrame(){}};
globalThis.requestAnimationFrame=window.requestAnimationFrame;
globalThis.cancelAnimationFrame=window.cancelAnimationFrame;
const body={label:'body'};
globalThis.document={body,addEventListener(){}};
const {Timer}=await import(pathToFileURL(path.join(root,'upstream/anime/src/timer/timer.js')));
const {ScrollObserver}=await import(pathToFileURL(path.join(root,'upstream/anime/src/events/scroll.js')));
const {isDomSymbol}=await import(pathToFileURL(path.join(root,'upstream/anime/src/core/consts.js')));
const dom=label=>({label,[isDomSymbol]:true});
const cases=[];
for(const mode of ['true','false','direct','body','explicit','timeline','persist']){
 const events=[];
 const observer=Object.assign(Object.create(ScrollObserver.prototype),{linked:null,_params:{},target:dom('explicit'),refresh(){events.push('refresh:'+this.target.label);}});
 if(mode==='explicit')observer._params.target=observer.target;
 const timer={_fps:60,_speed:1,_hasChildren:false,_autoplay:mode==='true'?true:mode==='false'?false:observer,
   reset(){events.push('reset');},resume(){events.push('resume');},pause(){events.push('pause');}};
 if(mode==='direct'||mode==='explicit'||mode==='persist')timer.targets=[{},dom('first'),dom('second')];
 if(mode==='body')timer.targets=[{}];
 if(mode==='timeline'){const second={targets:[dom('child-first'),dom('child-second')],_next:null};timer._head={targets:[{}],_next:second};}
 if(mode==='persist')timer.persist=false;
 Timer.prototype.init.call(timer);
 cases.push({mode,events,target:observer.target.label,linked:observer.linked===timer,persist:timer.persist??null});
}
const output=path.join(path.dirname(fileURLToPath(import.meta.url)),'..','tests','reference','scroll-construction-reference.json');
fs.writeFileSync(output,JSON.stringify({baselineCommit:'2c9cf8ea00329f6768c7d7902252ed977d75ce42',harness:'Unmodified Timer.prototype.init and ScrollObserver.prototype.link on explicit controller state. Reset, resume and layout refresh record method calls; actual DOM construction, clock property setters, frame scheduling and CSS layout excluded.',cases},null,2)+'\n');
console.log('scroll construction oracle: '+cases.length+' cases -> '+output);
