import {test} from 'node:test';
import assert from 'node:assert/strict';
import {openAssetArchive} from '../../runtime/wwwroot/asset-archive.js';
import {hostFonts} from '../../runtime/wwwroot/host-fonts.js';
import {GlyphStorage} from '../../runtime/wwwroot/glyph-storage.js';

function setup({quota=false,short=false}={}) {
  let saved=null,version='first';
  const calls=[];
  const policies={};
  const storage={open:async()=>({
    match:async()=>saved?.clone(),
    delete:async()=>{saved=null;},
    put:async(_url,response)=>{
      if(quota)throw Error('Quota exceeded');
      const bytes=await response.arrayBuffer();
      saved=new Response(bytes,{headers:response.headers});
    },
  })};
  const request=async(_url,options={})=>{
    const method=options.method??'GET';
    calls.push(method);
    assert.equal(options.cache,'no-store');
    const headers={'etag':version,'x-slop-decoded-length':'3','content-encoding':'gzip'};
    if(policies[method]) headers['cache-control']=policies[method];
    return new Response(options.method==='HEAD'?null:new Uint8Array(short?[1]:[1,2,3]),{headers});
  };
  return {storage,request,calls,policies,change(){version='second';}};
}
async function read(fixture) {
  const {response,stored}=await openAssetArchive('https://example.test/assets/Eco.zip',fixture.request,fixture.storage);
  const bytes=await response.arrayBuffer();await stored;return new Uint8Array(bytes);
}

test('warm archive startup revalidates without another body transfer',async()=>{
  const f=setup();
  assert.deepEqual(await read(f),new Uint8Array([1,2,3]));
  assert.deepEqual(await read(f),new Uint8Array([1,2,3]));
  assert.deepEqual(f.calls,['HEAD','GET','HEAD']);
  f.change();await read(f);
  assert.deepEqual(f.calls,['HEAD','GET','HEAD','HEAD','GET']);
});
test('no-store on HEAD or GET bypasses and evicts an existing archive',async()=>{
  for(const method of ['HEAD','GET']) {
    const f=setup();
    await read(f);
    // Make the GET necessary independently of the policy on its response.
    if(method==='GET') f.change();
    f.policies[method]='private, No-Store';
    assert.deepEqual(await read(f),Uint8Array.of(1,2,3));
    assert.deepEqual(f.calls,['HEAD','GET','HEAD','GET']);
    assert.equal(await (await f.storage.open()).match(),undefined);
    await read(f);
    assert.deepEqual(f.calls.slice(-2),['HEAD','GET']);
  }
});
test('legacy no-store archive is replaced even when HEAD matches its validator',async()=>{
  const f=setup();
  await (await f.storage.open()).put('archive',new Response(Uint8Array.of(9,9,9),{headers:{
    etag:'first','x-slop-decoded-length':'3','cache-control':'no-store',
  }}));
  assert.deepEqual(await read(f),Uint8Array.of(1,2,3));
  await read(f);
  assert.deepEqual(f.calls,['HEAD','GET','HEAD']);
});
test('quota and unavailable storage retain successful startup',async()=>{
  const f=setup({quota:true});await read(f);await read(f);
  assert.deepEqual(f.calls,['HEAD','GET','HEAD','GET']);
  f.storage=undefined;await read(f);
  assert.equal(f.calls.at(-1),'GET');
});

test('denied CacheStorage getter permits network archive and font reads and local glyph rendering',async t=>{
  const previous=Object.getOwnPropertyDescriptor(globalThis,'caches');
  Object.defineProperty(globalThis,'caches',{configurable:true,get(){throw Error('Storage denied');}});
  t.after(()=>{
    if(previous)Object.defineProperty(globalThis,'caches',previous);
    else delete globalThis.caches;
  });
  const f=setup();
  assert.deepEqual(await read({...f,storage:undefined}),new Uint8Array([1,2,3]));
  assert.deepEqual(f.calls,['GET']);
  const bytes=new Uint8Array([1,2,3]);
  const hash=Buffer.from(await crypto.subtle.digest('SHA-256',bytes)).toString('hex');
  assert.deepEqual(await hostFonts({},async()=>new Response(bytes)).file(hash),bytes);
  const glyph=new GlyphStorage();
  assert.equal(await glyph.render({scalar:200},()=>'{"missing":true}'),'{"missing":true}');
});

function streamingArchive(cache) {
  const chunks=16;
  let produced=0,canceled=false;
  const request=async(_url,options)=>{
    const headers={etag:'v1','x-slop-decoded-length':String(chunks)};
    if(options?.method==='HEAD')return new Response(null,{headers});
    return new Response(new ReadableStream({
      pull(controller) {
        controller.enqueue(Uint8Array.of(++produced));
        if(produced===chunks)controller.close();
      },
      cancel(){canceled=true;},
    }),{headers});
  };
  return {open:()=>openAssetArchive('archive',request,{open:async()=>cache}),
    produced:()=>produced,canceled:()=>canceled};
}

test('archive streaming bounds read-ahead for both fast and stalled cache readers',async()=>{
  for(const stall of [false,true]) {
    let release,saved;
    const gate=new Promise(resolve=>{release=resolve;});
    const f=streamingArchive({match:async()=>null,async put(_url,response){
      if(stall)await gate;
      saved=new Uint8Array(await response.arrayBuffer());
    }});
    const {response,stored}=await f.open();
    const reading=stall ? response.arrayBuffer() : null;
    await new Promise(setImmediate);
    assert.ok(f.produced()<=4,'A single fast consumer drained the archive');
    release();
    const received=new Uint8Array(await (reading??response.arrayBuffer()));
    await stored;
    assert.equal(received.length,16);
    assert.deepEqual(saved,received);
  }
});

test('canceling managed archive consumption aborts the cache copy and the upstream reader',async()=>{
  let failed=false;
  const f=streamingArchive({match:async()=>null,async put(_url,response){
    try { await response.arrayBuffer(); } catch(error) {failed=true;throw error;}
  }});
  const {response,stored}=await f.open();
  const reader=response.body.getReader();
  await reader.read();
  await reader.cancel(Error('Managed startup failed'));
  await stored;
  await new Promise(setImmediate);
  assert.equal(failed,true);
  assert.equal(f.canceled(),true);
});

test('quota failure after a partial cache read unblocks the remaining managed upload',async()=>{
  let rejectWrite,started;
  const writing=new Promise(resolve=>{started=resolve;});
  const f=streamingArchive({match:async()=>null,async put(_url,response){
    const reader=response.body.getReader();
    await reader.read();
    // Storage can abandon the locked body without reading the next chunk.
    await new Promise((_resolve,reject)=>{rejectWrite=reject;started();});
  }});
  const {response,stored}=await f.open();
  const reading=response.arrayBuffer();
  await writing;await new Promise(setImmediate);
  rejectWrite(Error('Quota exceeded'));
  assert.equal((await reading).byteLength,16);
  await stored;
});
test('truncated archive never becomes a usable cache entry',async()=>{
  const f=setup({short:true});
  await assert.rejects(read(f),/Incomplete cached asset transfer/);
  await assert.rejects(read(f),/Incomplete cached asset transfer/);
  assert.deepEqual(f.calls,['HEAD','GET','HEAD','GET']);
});
test('a deployment during HEAD and GET does not publish the wrong identity',async()=>{
  const f=setup();let writes=0;
  f.storage={open:async()=>({match:async()=>null,put:async()=>{writes++;}})};
  const request=f.request;f.request=async(url,options)=>{const response=await request(url,options);if(options?.method==='HEAD')f.change();return response;};
  await read(f);assert.equal(writes,0);
});
