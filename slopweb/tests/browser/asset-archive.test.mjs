import {test} from 'node:test';
import assert from 'node:assert/strict';
import {openAssetArchive} from '../../runtime/wwwroot/asset-archive.js';

function setup({quota=false,short=false}={}) {
  let saved=null,version='first';
  const calls=[];
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
    calls.push(options.method??'GET');
    const headers={'etag':version,'x-slop-decoded-length':'3','content-encoding':'gzip'};
    return new Response(options.method==='HEAD'?null:new Uint8Array(short?[1]:[1,2,3]),{headers});
  };
  return {storage,request,calls,change(){version='second';}};
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
test('quota and unavailable storage retain successful startup',async()=>{
  const f=setup({quota:true});await read(f);await read(f);
  assert.deepEqual(f.calls,['HEAD','GET','HEAD','GET']);
  f.storage=undefined;await read(f);
  assert.equal(f.calls.at(-1),'GET');
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
