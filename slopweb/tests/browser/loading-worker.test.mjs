import test from 'node:test';
import assert from 'node:assert/strict';
import {Worker} from 'node:worker_threads';

test('loading animates the visible loop through a stalled depth pack and transfers decoded frames', async t=>{
  const paints=new Int32Array(new SharedArrayBuffer(8));
  const worker=new Worker(new URL('./loading-worker-fixture.mjs',import.meta.url),{workerData:paints.buffer});
  t.after(()=>worker.terminate());
  const background=new Promise((resolve,reject)=>{
    worker.on('message',data=>{if(data.type==='background')resolve(data);});
    worker.on('error',reject);
  });
  worker.postMessage({type:'start',viewport:{width:800,height:600,uiScale:1}});
  // Block this thread through several animation ticks, including after all
  // startup callbacks (atlas, preview, background and quotes) could have painted.
  const deadline=performance.now()+5000;
  for(let count=Atomics.load(paints,0);count<10 || Atomics.load(paints,1)<8;count=Atomics.load(paints,0)){
    const remaining=deadline-performance.now();
    assert.ok(remaining>0,'Worker stopped painting during synchronous startup');
    Atomics.wait(paints,0,count,remaining);
  }
  assert.ok(Atomics.load(paints,1)>=8,'Loop must keep advancing before the remaining depths arrive');
  worker.postMessage({type:'take'});
  const result=await background;
  assert.equal(result.frames.images.length,6);
  assert.equal(result.frames.images[0].byteLength,4);
  assert.ok(result.started>=performance.timeOrigin);
});

test('page transfers its visible canvas and keeps the worker alive for pending background handoff',async t=>{
  const {startLoading}=await import('../../runtime/wwwroot/loading.js');
  const surface={},messages=[];
  let instance, terminated=false, removed=false;
  const canvas={dataset:{},setAttribute(){},transferControlToOffscreen:()=>surface,remove(){removed=true;}};
  const view=new EventTarget();
  const document={body:{classList:{add(){},remove(){}},style:{setProperty(){}},prepend(){}},createElement:()=>canvas};
  const globals={window:view,document,requestAnimationFrame:cb=>setImmediate(cb),Worker:class{
    constructor(url,options){
      assert.ok(url.pathname.endsWith('/loading-worker.js'));
      assert.equal(options.type,'module');
      instance=this;
    }
    postMessage(message,transfer){messages.push({message,transfer});}
    terminate(){terminated=true;}
  }};
  for(const [name,value] of Object.entries(globals)){
    const previous=Object.getOwnPropertyDescriptor(globalThis,name);
    Object.defineProperty(globalThis,name,{value,configurable:true});
    t.after(()=>{if(previous)Object.defineProperty(globalThis,name,previous);else delete globalThis[name];});
  }
  const controller=await startLoading(new URLSearchParams());
  assert.equal(messages[0].message.canvas,surface);
  assert.deepEqual(messages[0].transfer,[surface]);
  controller.status('Loading assemblies');
  const retained=controller.takeBackground();
  controller.finish();
  assert.equal(removed,true);
  assert.equal(terminated,false);
  assert.deepEqual(messages.map(m=>m.message.type),['start','status','take']);
  instance.onmessage({data:{type:'background',frames:null}});
  assert.equal(await retained.ready,null);
  assert.equal(terminated,true);
});
