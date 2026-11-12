// Exercise loading readiness with deterministic frames and delayed asset delivery.
import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const source=readFileSync(new URL('../../runtime/wwwroot/loading.js',import.meta.url),'utf8')
  .replace(/^import .*;\n/gm,'').replace('export async function','async function');
const factory=new Function('workspaceViewport','loadBackground','tintPixels','loadingQuotes',
  'document','Image','ImageData','requestAnimationFrame','cancelAnimationFrame','fetch','performance',
  source+'\nreturn startLoading;');

test('animation and quotes wait for all presentation assets and start together',async()=>{
  let now=1000, finishBackground, atlas, nextFrame;
  const trace=[];
  const context=new Proxy({}, {get:(_,key)=>key==='getImageData'?()=>({data:[]}):()=>{}});
  const canvas=()=>({width:32,height:64,dataset:{},setAttribute(){},remove(){},getContext:()=>context});
  const document={body:{classList:{add(){},remove(){}},style:{setProperty(){}},prepend(){}},createElement:canvas};
  const start=factory(()=>({width:800,height:600,uiScale:1}),(_signal,original)=>{
    original({draw(){trace.push('original');},dispose(){}});
    return new Promise(resolve=>{finishBackground=resolve;});
  },()=>[],()=>({lines:[],advance(time){trace.push(['quotes',time]);return true;}}),document,
  class {constructor(){atlas=this;}},class {},callback=>{nextFrame=callback;return 1;},()=>{},
  async()=>({ok:true,text:async()=>'quote'}),{now:()=>now});
  const pending=start(new URLSearchParams());
  await new Promise(resolve=>setTimeout(resolve,0));
  atlas.onload();
  assert.ok(trace.includes('original'));
  assert.equal(trace.some(item=>Array.isArray(item)),false);
  now=2000;
  finishBackground({draw(){trace.push('animated');},advance(time){trace.push(['animation',time]);return true;},dispose(){}});
  await new Promise(resolve=>setTimeout(resolve,0));
  assert.deepEqual(trace.filter(Array.isArray),[['quotes',2]]);
  // Release the compositor gate, then run the loading update at the same origin.
  nextFrame();nextFrame();
  const loading=await pending;
  assert.equal(loading.takeBackground().started,2);
  loading.finish();
});
