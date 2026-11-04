// Anime.js MIT, Copyright (c) 2025 Julian Garnier; see canonical THIRD-PARTY-NOTICES.md.
import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {writeFileSync} from 'node:fs';
import {resolve,dirname} from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
const lane=dirname(fileURLToPath(import.meta.url));
const root=process.env.RANVIER_ANIME_ROOT;
assert.ok(root);
const upstream=resolve(root,'upstream/anime');
const git=(...args)=>execFileSync('rtk',['proxy','git','-C',upstream,...args],{encoding:'utf8'}).trim();
assert.equal(git('rev-parse','HEAD'),'2c9cf8ea00329f6768c7d7902252ed977d75ce42');
assert.equal(git('status','--porcelain'),'');
const {parseInlineTransforms,buildTransformString}=await import(pathToFileURL(resolve(upstream,'src/core/transforms.js')));
const {transformsSymbol}=await import(pathToFileURL(resolve(upstream,'src/core/consts.js')));
const inputs=['','none','rotate(45deg) translate(10px,20px)','translateX(10px) rotate(5deg) translateY(4em)',
 'translate3d(1px,2%,3rem) rotate3d(1,0,0,45deg) scale3d(1,2,3)',
 'scale(2) scaleX(3) scaleY(4) scaleZ(5)','scale(2,3) translate(10px)',
 'translate(calc(10px + var(--n, 2px)),min(20px, 30px)) rotate(3deg)',
 'matrix(1,0,0,1,40,50) skew(4deg, 5deg) perspective(200px)',
 'matrix3d(1,0,0,0,0,1,0,0,0,0,1,0,1,2,3,1) rotateZ(4deg)',
 'translateX(2px) translateX(8px) scaleX(2) scaleY(2)',
 '  translate(3px, 4px)  rotate(5deg)  ',
 'rotate3d(0,1,0,30deg)','scaleZ(2) translateY(4px)','translate(5%) scale(0.5)'];
const properties=['perspective','translateX','translateY','translateZ','translate3d','rotate','rotateX','rotateY','rotateZ','rotate3d','scale','scaleX','scaleY','scaleZ','skew','skewX','skewY'];
const fixtures=inputs.map(inline=>{
 const target={style:{transform:inline},[transformsSymbol]:{}};
 const reads=Object.fromEntries(properties.map(property=>[property,parseInlineTransforms(target,property,{})]));
 const parsed={...target[transformsSymbol]};
 const built=buildTransformString(parsed);
 const moved={...parsed,translateX:'7px',translateY:'9px'};
 return {inline,reads,parsed,built,moved:buildTransformString(moved)};
});
assert.equal(fixtures[2].built,'translate(10px,20px) rotate(45deg) ');
assert.equal(fixtures[4].built,'translate3d(1px,2%,3rem) rotate3d(1,0,0,45deg) scale3d(1,2,3) ');
assert.equal(git('status','--porcelain'),'');
writeFileSync(resolve(lane,'transform-reference.json'),JSON.stringify({upstreamCommit:git('rev-parse','HEAD'),scope:'Actual unmodified core/transforms.js parser and serializer; synthetic style object, no native CSS parsing proof.',fixtures},null,2)+'\n');
console.log(`Verified ${fixtures.length} source transform parse/build/move cases`);
