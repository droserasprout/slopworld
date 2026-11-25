// Exercise loading readiness with deterministic frames and delayed asset delivery.
import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const source=readFileSync(new URL('../../runtime/wwwroot/loading-renderer.js',import.meta.url),'utf8')
  .replace(/^import .*;\n/gm,'').replace('export function','function');
const factory=new Function('loadBackground','tintPixels','loadingQuotes',
  'ImageData','requestAnimationFrame','cancelAnimationFrame','fetch','performance',
  source+'\nreturn renderLoading;');

const tick=()=>new Promise(setImmediate);
function deferred() {
  let resolve,reject;
  const promise=new Promise((yes,no)=>{resolve=yes;reject=no;});
  return {promise,resolve,reject};
}
function fixture() {
  const atlas=deferred();
  let now=1000, nextFrame=0, backgroundSignal;
  const frames=new Map(),background=deferred(),tips=deferred();
  const trace=[];
  const context=new Proxy({}, {get:(_,key)=>key==='getImageData'?()=>({data:[]}):()=>{}});
  const canvas=()=>({width:32,height:64,dataset:{},setAttribute(){},remove(){},getContext:()=>context});
  const start=factory((_signal,original)=>{
    backgroundSignal=_signal;
    original({draw(){trace.push('original');},dispose(){trace.push('dispose original');}});
    return background.promise;
  },()=>[],()=>({lines:[],advance(time){trace.push(['quotes',time]);return true;}}),class {},callback=>{frames.set(++nextFrame,callback);return nextFrame;},id=>frames.delete(id),
  ()=>tips.promise,{now:()=>now});
  const pending=start(canvas(),{width:800,height:600,uiScale:1},false,canvas,()=>atlas.promise);
  const frame=time=>{
    now=time;
    const callbacks=[...frames.values()];frames.clear();
    for(const callback of callbacks)callback();
  };
  return {trace,frame,tips,background,signal:()=>backgroundSignal,atlas:()=>atlas.resolve({width:32,height:64}),
    animation:{draw(){trace.push('animated');},advance(time){trace.push(['animation',time]);return true;},dispose(){trace.push('dispose animation');}},
    async loading(){frame(now);frame(now);return pending;}};
}
const quoteResponse={ok:true,text:async()=>'quote'};

test('quotes scroll while background frames are delayed; background retains its own start time',async()=>{
  const f=fixture(),loading=await f.loading();
  f.tips.resolve(quoteResponse);f.atlas();await tick();
  f.frame(1500);
  assert.ok(f.trace.includes('original'));
  assert.ok(f.trace.some(item=>Array.isArray(item)&&item[0]==='quotes'));
  assert.equal(f.trace.includes('animated'),false);
  f.frame(2000);f.background.resolve(f.animation);await tick();
  f.trace.length=0;f.frame(2500);
  assert.deepEqual(f.trace.filter(Array.isArray),[['animation',.5],['quotes',2.5]]);
  assert.equal((await loading.takeBackground().ready).started,2);
  loading.finish();
  assert.equal(f.trace.includes('dispose animation'),false); // Transferred to workspace.
  f.animation.dispose();
});

test('background animates when quotes fail or the font atlas never arrives',async()=>{
  for(const missing of ['quotes','atlas']) {
    const f=fixture(),loading=await f.loading();
    if(missing==='quotes') {f.tips.reject(Error('unavailable'));f.atlas();}
    else f.tips.resolve(quoteResponse);
    f.background.resolve(f.animation);await tick();
    f.trace.length=0;f.frame(2000);
    assert.deepEqual(f.trace.filter(Array.isArray),[['animation',1]]);
    assert.ok(f.trace.includes('animated'));
    loading.finish();await tick();
    assert.ok(f.trace.includes('dispose animation'));
  }
});

test('background failure does not stop ready quotes',async()=>{
  const f=fixture(),loading=await f.loading();
  f.background.reject(Error('unavailable'));f.tips.resolve(quoteResponse);f.atlas();await tick();
  f.frame(2000);
  assert.ok(f.trace.some(item=>Array.isArray(item)&&item[0]==='quotes'));
  loading.finish();
});

test('finishing loading retires delayed assets without painting again',async()=>{
  const f=fixture(),loading=await f.loading();
  loading.finish();f.trace.length=0;
  f.background.resolve(f.animation);f.tips.resolve(quoteResponse);f.atlas();await tick();
  f.frame(2000);
  assert.deepEqual(f.trace,['dispose animation']);
});


test('workspace takes pending background without restarting or aborting its downloads',async()=>{
  const f=fixture(),loading=await f.loading();
  const retained=loading.takeBackground();
  loading.finish();f.trace.length=0;
  assert.equal(f.signal().aborted,false);
  f.frame(2000);f.background.resolve(f.animation);
  const value=await retained.ready;
  assert.equal(value.animation,f.animation);
  assert.equal(value.started,2);
  assert.deepEqual(f.trace,[]);
  value.animation.dispose();retained.cancel();
  assert.equal(f.signal().aborted,true);
});
