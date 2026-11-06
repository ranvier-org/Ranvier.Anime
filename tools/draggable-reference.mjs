// Derived behavior fixtures: Anime.js MIT, copyright Julian Garnier.
import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {readFileSync,writeFileSync} from 'node:fs';
import {resolve,dirname} from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
const lane=dirname(fileURLToPath(import.meta.url));
const root=process.env.RANVIER_ANIME_ROOT;
assert.ok(root,'RANVIER_ANIME_ROOT required');
const upstream=resolve(root,'upstream/anime');
const git=(...args)=>execFileSync('rtk',['proxy','git','-C',upstream,...args],{encoding:'utf8'}).trim();
assert.equal(git('rev-parse','HEAD'),'2c9cf8ea00329f6768c7d7902252ed977d75ce42');
assert.equal(git('status','--porcelain'),'');
let clock=0;
Date.now=()=>clock;
const {Draggable}=await import(pathToFileURL(resolve(upstream,'src/draggable/draggable.js')));
const {spring}=await import(pathToFileURL(resolve(upstream,'src/easings/spring/index.js')));
const fixtures=[];
const record=(family,input,output)=>fixtures.push({family,input,output});
for(const [min,max,multiplier] of [[0,50,1],[1,5,2],[0,0,1]]) {
 const self=Object.assign(Object.create(Draggable.prototype),{velocityTime:0,velocity:0,velocityStack:[0,0,0],velocityStackIndex:0,minVelocity:min,maxVelocity:max,velocityMultiplier:multiplier});
 for(const [time,dx,dy] of [[16,100,0],[17,3,4],[34,34,0],[51,0,0],[68,0,0],[85,0,0]]) {
  clock=time;
  const velocity=self.computeVelocity(dx,dy);
  record('velocity',{min,max,multiplier,time,dx,dy},{velocity,stack:[...self.velocityStack],index:self.velocityStackIndex,lastTime:self.velocityTime});
 }
}
for(const disabled of [[0,0],[1,0],[0,1],[1,1]]) for(const [x,y] of [[0,0],[-11,0],[0,21],[31,-11],[30,20]]) {
 const bounds=[-10,30,20,-10];
 const self={contained:true,disabled};
 record('bounds',{bounds,disabled,x,y},Draggable.prototype.isOutOfBounds.call(self,bounds,x,y));
}
for(const disabled of [[0,0],[1,0],[0,1]]) for(const threshold of [3,7]) for(const [x,y] of [[3,0],[4,0],[0,8],[-8,0]]) {
 let dragCalls=0,resumes=0,prevents=0;
 const self={grabbed:true,dragged:false,released:false,triggerStyles:{},$trigger:{addEventListener(){}},transforms:{normalizePoint:(x,y)=>({x,y})},disabled,dragThreshold:threshold,pointer:Array(8).fill(0),updateTicker:{resume(){resumes++}},onDrag(){dragCalls++}};
 Draggable.prototype.handleMove.call(self,{clientX:x,clientY:y,target:null,cancelable:true,preventDefault(){prevents++}});
 record('threshold',{disabled,threshold,x,y},{dragged:self.dragged,dragCalls,resumes,prevents,pointer:[...self.pointer]});
}
for(const [x,y,velocity,angle,friction,snapX,snapY] of [[10,20,1,0,.8,0,0],[10,20,2,Math.PI/4,.8,10,20],[150,-50,0,0,1,0,0],[-20,30,3,Math.PI,.2,[0,25,70],0]]) {
 const calls=[],events=[];
 const self=Object.assign(Object.create(Draggable.prototype),{grabbed:true,dragged:true,disabled:[0,0],pointer:Array(8).fill(0),containerBounds:[0,100,100,0],snapped:[0,0],releaseXSpring:spring({mass:1,stiffness:80,damping:20}),releaseYSpring:spring({mass:1,stiffness:80,damping:20}),releaseEase:t=>t,hasReleaseSpring:true,overshootCoords:{},releaseContainerFriction:friction,dragSpeed:1,snapX,snapY,xProp:'x',yProp:'y',animate:{x:(v,d)=>calls.push(['x',v,d]),y:(v,d)=>calls.push(['y',v,d])},updateTicker:{pause(){}},$trigger:{removeEventListener(){}},scrollInView(){},computeVelocity:()=>velocity,onSnap(){events.push('snap')},onRelease(){events.push('release')},contained:true});
 Object.defineProperties(self,{x:{value:x},y:{value:y}});
 self.pointer[0]=Math.cos(angle);self.pointer[1]=Math.sin(angle);
 // doc is undefined in a non-browser import: release physics and callbacks run before listener cleanup.
 let cleanupError=null;
 try{self.handleUp()}catch(e){cleanupError=e.message}
 assert.equal(events.at(-1),'release');
 record('release',{x,y,velocity,angle,friction,snapX,snapY},{destX:self.destX,destY:self.destY,calls,events,grabbed:self.grabbed,released:self.released,cleanupError});
}
assert.equal(git('status','--porcelain'),'');
writeFileSync(resolve(lane,'../tests/reference/draggable-reference.json'),JSON.stringify({upstreamCommit:git('rev-parse','HEAD'),license:'Anime.js MIT, copyright Julian Garnier; see canonical THIRD-PARTY-NOTICES.md and upstream/anime/LICENSE.md',scope:'Actual unmodified prototype methods. Synthetic event/geometry/animatable dependencies; no browser/native rendering proof. handleUp listener cleanup error recorded after physics/callbacks because imported doc is null in Node.',fixtures},null,2)+'\n');
console.log(`Captured ${fixtures.length} dragging source method cases`);
