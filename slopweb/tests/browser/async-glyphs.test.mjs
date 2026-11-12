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
  assert.equal(await new GlyphStorage(storage).render(request,render),glyph);assert.equal(renders,1);
  for(const change of [{density:2},{size:18},{flags:545},{hash:'B'.repeat(64)}])await first.render({...request,...change},render);
  assert.equal(renders,5);
  entries.set(first.key(request),new Response('{"width":1}'));
  assert.equal(await first.render(request,render),glyph);assert.equal(renders,6);
});
test('denied persistence preserves rendering and entry count stays bounded',async()=>{
  assert.equal(await new GlyphStorage({open:async()=>{throw Error('denied');}}).render(request,()=>glyph),glyph);
  const {storage,entries}=storageFixture(),cache=new GlyphStorage(storage);
  for(let i=0;i<512;i++)entries.set('old'+i,new Response(glyph));
  await cache.render(request,()=>glyph);
  assert.equal(entries.size,512);assert.equal(entries.has('old0'),false);
});
