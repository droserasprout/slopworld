import {test} from 'node:test';
import assert from 'node:assert/strict';
import {asyncGlyphs} from '../../runtime/wwwroot/async-glyphs.js';
import {GlyphStorage} from '../../runtime/wwwroot/glyph-storage.js';

const glyph=JSON.stringify({advance:8,x:0,y:-8,ascent:10,descent:-2,width:1,height:1,pixels:'/w=='});
const request={hash:'A'.repeat(64),scalar:0x2928,size:14,density:1,flags:513};
const json=JSON.stringify(request);
function fixture() {
  const messages=[],calls=[];let wakes=0;
  const worker={postMessage:message=>messages.push(message),terminate:()=>calls.push('terminate')};
  const local={call:value=>{calls.push(value);return 'local';},install:()=>calls.push('install'),close:()=>calls.push('close')};
  const raster=asyncGlyphs(local,worker,()=>wakes++);
  const reply=(message,value=glyph)=>worker.onmessage({data:{id:message.id,value}});
  return {raster,messages,calls,worker,reply,wakes:()=>wakes};
}
const tick=()=>new Promise(setImmediate);
test('startup drain batches slow replies and invalidates before returning to the next draw',async t=>{
  t.mock.timers.enable({apis:['setTimeout']});
  const f=fixture();
  for(let scalar=128;scalar<131;scalar++)f.raster.call(JSON.stringify({...request,scalar}));
  const work=f.raster.settle();
  for(const message of f.messages.slice(0,2)) {
    f.reply(message);await tick();
    t.mock.timers.tick(100);
    assert.equal(f.wakes(),0);
  }
  f.reply(f.messages[2]);
  assert.equal(await work,true);
  assert.equal(f.wakes(),1);
  t.mock.timers.tick(100);assert.equal(f.wakes(),1);
  assert.equal(await f.raster.settle(),false);
  // Live glyph delivery resumes its bounded notification timer after startup.
  f.raster.call(json);f.reply(f.messages.at(-1));await tick();
  assert.equal(f.wakes(),1);
  t.mock.timers.tick(16);assert.equal(f.wakes(),2);
  f.raster.close();
});
test('draining completed replies consumes the queued invalidation exactly once',async t=>{
  t.mock.timers.enable({apis:['setTimeout']});
  const f=fixture();f.raster.call(json);f.reply(f.messages[0]);await tick();
  assert.equal(f.wakes(),0);
  assert.equal(await f.raster.settle(),true);assert.equal(f.wakes(),1);
  t.mock.timers.tick(100);assert.equal(f.wakes(),1);
  f.raster.close();
});
test('overlapping drains coalesce failure notification and teardown suppresses it',async t=>{
  t.mock.timers.enable({apis:['setTimeout']});
  const f=fixture();f.raster.call(json);
  const first=f.raster.settle(),second=f.raster.settle();
  f.worker.onerror();
  assert.deepEqual(await Promise.all([first,second]),[true,true]);
  assert.equal(f.wakes(),1);f.raster.close();
  const stopped=fixture();stopped.raster.call(json);
  const draining=stopped.raster.settle();stopped.raster.close();
  assert.equal(await draining,false);
  t.mock.timers.tick(100);assert.equal(stopped.wakes(),0);
});
test('unusual glyphs never rasterize on the UI thread; concurrent requests reuse one completion',async()=>{
  const f=fixture();
  assert.equal(f.raster.call(json),'{"pending":true}');
  assert.equal(f.raster.call(json),'{"pending":true}');
  assert.equal(f.messages.length,1);assert.deepEqual(f.calls,[]);
  f.reply(f.messages[0]);await f.raster.settle();
  assert.equal(f.raster.call(json),glyph);
  assert.equal(f.raster.call(JSON.stringify({...request,scalar:65})),'local');
  await new Promise(resolve=>setTimeout(resolve,25));assert.equal(f.wakes(),1);
  f.raster.close();f.raster.close();assert.deepEqual(f.calls.slice(-2),['terminate','close']);
});
test('font installation waits for worker and disposal rejects late publication',async()=>{
  const f=fixture();const pending=f.raster.install(request.hash,new Uint8Array([1]));
  assert.deepEqual(f.calls,[]);f.reply(f.messages[0],'{}');await pending;
  assert.deepEqual(f.calls,['install']);
  f.raster.call(json);f.raster.close();f.reply(f.messages[1]);await f.raster.settle();
  assert.equal(f.wakes(),0);
});
test('worker failures settle requests and cannot leave glyphs pending forever',async()=>{
  const f=fixture();f.raster.call(json);f.worker.onerror();await f.raster.settle();
  assert.throws(()=>f.raster.call(json),/failed/);f.raster.close();
});
test('glyph request admission is bounded and retries deferred work after completion',async()=>{
  const f=fixture();
  for(let scalar=128;scalar<428;scalar++)f.raster.call(JSON.stringify({...request,scalar}));
  assert.equal(f.messages.length,256);
  for(const message of f.messages)f.reply(message);
  await f.raster.settle();f.raster.call(JSON.stringify({...request,scalar:427}));
  assert.equal(f.messages.length,257);f.raster.close();
});
function storageFixture() {
  const entries=new Map();
  const cache={match:async key=>entries.get(key)?.clone(),put:async(key,value)=>entries.set(key,value.clone()),
    keys:async()=>[...entries.keys()],delete:async key=>entries.delete(key)};
  return {entries,storage:{open:async()=>cache}};
}
test('rendered glyphs survive reload; corrupt data and changed density/style/font rerender',async()=>{
  const {storage,entries}=storageFixture();let renders=0;
  const render=()=>{renders++;return glyph;};
  const first=new GlyphStorage(storage);assert.equal(await first.render(request,render),glyph);
  await first.flush();
  assert.equal(await new GlyphStorage(storage).render(request,render),glyph);assert.equal(renders,1);
  for(const change of [{density:2},{size:18},{flags:545},{hash:'B'.repeat(64)}])await first.render({...request,...change},render);
  assert.equal(renders,5);
  await first.flush();
  entries.set(first.key(request),new Response('{"width":1}'));
  assert.equal(await first.render(request,render),glyph);assert.equal(renders,6);
});
test('denied persistence preserves rendering and entry count stays bounded',async()=>{
  assert.equal(await new GlyphStorage({open:async()=>{throw Error('denied');}}).render(request,()=>glyph),glyph);
  const {storage,entries}=storageFixture(),cache=new GlyphStorage(storage);
  for(let i=0;i<512;i++)entries.set('old'+i,new Response(glyph));
  await cache.render(request,()=>glyph);
  await cache.flush();
  assert.equal(entries.size,512);assert.equal(entries.has('old0'),false);
});

test('slow persistence cannot delay glyph replies; pending writes are bounded and reusable',{timeout:5000},async()=>{
  let release;const gate=new Promise(resolve=>{release=resolve;});
  const entries=new Map();let writes=0,renders=0;
  const cache={match:async()=>undefined,put:async(key,value)=>{writes++;await gate;entries.set(key,value);},
    keys:async()=>[...entries.keys()],delete:async key=>entries.delete(key)};
  const storage=new GlyphStorage({open:async()=>cache});
  const render=()=>{renders++;return glyph;};
  // Every reply settles while the first write remains explicitly blocked.
  for(let scalar=128;scalar<228;scalar++)assert.equal(await storage.render({...request,scalar},render),glyph);
  assert.equal(writes,1);assert.equal(storage.pending.size,32);
  assert.equal(await storage.render({...request,scalar:128},render),glyph);
  assert.equal(renders,100);
  release();await storage.flush();
  assert.equal(writes,32);assert.equal(storage.pending.size,0);
  await storage.render(request,render);await storage.flush();assert.equal(writes,33);
});

test('failed writes are drained without poisoning later persistence',async()=>{
  let fail=true,writes=0;
  const cache={match:async()=>undefined,put:async()=>{writes++;if(fail)throw Error('quota');},keys:async()=>[]};
  const storage=new GlyphStorage({open:async()=>cache});
  assert.equal(await storage.render(request,()=>glyph),glyph);await storage.flush();
  fail=false;
  assert.equal(await storage.render(request,()=>glyph),glyph);await storage.flush();
  assert.equal(writes,2);assert.equal(storage.pending.size,0);
});
