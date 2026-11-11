import test from 'node:test';
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {loadWorkspacePortraits} from '../runtime/wwwroot/workspace-portraits.js';

function fixture() {
  const data=new Uint8Array([1,0,0,0,1,0,0,0,12,34,56,128]);
  const file=createHash('sha256').update(data).digest('hex')+'.rgba';
  const entry={session:'actual-session',file,width:1,height:1};
  return {data,entry};
}
test('missing portrait export leaves identity absent', async()=>{
  assert.equal(await loadWorkspacePortraits({WorkspacePortrait(){assert.fail('Unexpected portrait');}},
    async()=>({status:404})),0);
});
test('validated snapshot passes unchanged session identity and pixel bytes to managed code', async()=>{
  const {data,entry}=fixture(), installed=[];
  const fetch=async path=>path.endsWith('manifest.json')
    ? {ok:true,json:async()=>({version:1,entries:[entry]})}
    : {ok:true,arrayBuffer:async()=>data.buffer};
  assert.equal(await loadWorkspacePortraits({WorkspacePortrait:(...args)=>installed.push(args)},fetch),1);
  assert.deepEqual(installed,[[entry.session,data]]);
});
test('unsafe paths and corrupt pixels never reach the managed portrait bank', async()=>{
  for(const corrupt of ['path','hash','header','truncated','duplicate']) {
    const {data,entry}=fixture();
    if(corrupt==='path')entry.file='../secret';
    if(corrupt==='hash')data[8]++;
    if(corrupt==='header')data[0]++;
    let installs=0;
    const fetch=async path=>path.endsWith('manifest.json')
      ? {ok:true,json:async()=>({version:1,entries:corrupt==='duplicate'?[entry,entry]:[entry]})}
      : {ok:true,arrayBuffer:async()=>corrupt==='truncated'?data.buffer.slice(0,-1):data.buffer};
    await assert.rejects(loadWorkspacePortraits({WorkspacePortrait(){installs++;}},fetch));
    assert.equal(installs,corrupt==='duplicate'?1:0);
  }
});
