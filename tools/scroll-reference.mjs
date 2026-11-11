// Anime.js v4.5.0, MIT, Copyright (c) 2025 Julian Garnier.
// Invoke unmodified source prototype methods with deterministic DOM measurements.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
const root = process.env.RANVIER_ANIME_ROOT;
if (!root) throw new Error('RANVIER_ANIME_ROOT is required');
const { ScrollObserver } = await import(pathToFileURL(path.join(root,'upstream/anime/src/events/scroll.js')));
const baselineCommit = '2c9cf8ea00329f6768c7d7902252ed977d75ce42';
function observer() {
  const target = {getBoundingClientRect:()=>({left:800,top:800,width:100,height:100})};
  const o = Object.create(ScrollObserver.prototype);
  const events=[];
  Object.assign(o, {target,container:{element:target,scale:1,left:0,top:0,scrollX:0,scrollY:0,width:600,height:600,scrollWidth:2400,scrollHeight:2400,backwardX:false,backwardY:false,wakeTicker:{restart(){}}},
    horizontal:false,enter:'end start',leave:'start end',ready:true,reverted:false,repeat:true,sync:false,syncEase:null,syncSmooth:null,
    linked:null,completed:false,began:false,isInView:false,forceEnter:false,hasEntered:false,prevProgress:0,_debug:false});
  for (const e of ['Enter','Leave','EnterForward','LeaveForward','EnterBackward','LeaveBackward','Update','Resize','SyncComplete']) o['on'+e]=()=>events.push(e);
  for (const e of ['Enter','Leave','EnterForward','LeaveForward','EnterBackward','LeaveBackward']) o['onSync'+e]=()=>{};
  o.revert=()=>{o.reverted=true;o.ready=false;};
  return {o,events};
}
const geometry=[];
for(const offset of [0,12.5,800,2300]) for(const pair of [
  ['end start','start end'],['center center','center end'],['max start','min end'],
  ['max-=20 start','min+=30 end'],['75% 25%','10% 90%'],['end+=12.5 start','start-=12.5 end']]) {
  const {o}=observer(); o.target.getBoundingClientRect=()=>({left:offset,top:offset,width:100,height:100});
  [o.enter,o.leave]=pair; o.updateBounds();
  geometry.push({offset,enter:pair[0],leave:pair[1],start:o.offsetStart,end:o.offsetEnd,distance:o.distance,coords:o.coords});
}
const traces=[];
for(const mode of ['disabled','methods','directional','linear','smooth','ease']) for(const positions of [
  [0,200,200,201,500,900,901,500,200,199], [0,1000,0], [200,201,200,201,900,900], [900,899,200,201,900]]) {
  const {o,events}=observer();o.updateBounds();
  let linkedProgress=0;
  const writes=[];
  const command=e=>events.push('command:'+e);
  if(mode!=='disabled') {
    o.sync=true;
    o.linked={duration:1000,get progress(){return linkedProgress;},get completed(){return linkedProgress===1;},seek(time,muted){writes.push([time,!!muted]); linkedProgress=Math.max(0,Math.min(1,time/1000));}};
    if(mode==='linear') o.syncSmooth=1;
    if(mode==='smooth') o.syncSmooth=0.5;
    if(mode==='ease') o.syncEase=p=>p*p;
    if(mode==='methods') {o.onSyncEnter=()=>command('Play');o.onSyncLeave=()=>command('Pause');}
    if(mode==='directional') for(const e of ['EnterForward','LeaveForward','EnterBackward','LeaveBackward']) o['onSync'+e]=()=>command(e);
  }
  const steps=[]; let prior=0;
  for(const scroll of positions) {
    events.length=0;writes.length=0;
    o.container.scrollY=scroll;if(scroll!==prior)o.container.backwardY=scroll<prior;prior=scroll;
    o.handleScroll();
    steps.push({scroll,events:[...events],writes:[...writes],progress:o.progress,previous:o.prevProgress,inView:o.isInView,began:o.began,completed:o.completed});
  }
  traces.push({mode,steps});
}
const output=path.join(path.dirname(fileURLToPath(import.meta.url)),'..','tests','reference','scroll-reference.json');
fs.writeFileSync(output,JSON.stringify({baselineCommit,methodHarness:'unmodified ScrollObserver.prototype.updateBounds/handleScroll, explicit measured geometry; constructor and native scheduling excluded',geometry,traces},null,2)+'\n');
console.log(`scroll oracle: ${geometry.length} geometry cases, ${traces.length} traces -> ${output}`);
