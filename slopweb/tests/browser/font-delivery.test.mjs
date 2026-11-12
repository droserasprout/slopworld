import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {fallbackFonts,fontRequests} from '../../runtime/wwwroot/font-delivery.js';
import {HostFonts,covers} from '../../runtime/wwwroot/host-fonts.js';
const bytes=new TextEncoder().encode('font');
const hash=createHash('sha256').update(bytes).digest('hex');
const catalog={version:1,revision:'v1',faces:[{hash,family:'Fallback',loaderFlags:513,coverage:[[65,90],[0x2928,0x2928]]}],order:[hash],preload:[hash]};
const response=()=>({ok:true,json:async()=>catalog,arrayBuffer:async()=>bytes.buffer});
function fixture(fetcher) {
  let pending=JSON.stringify({id:1,family:'UI',scalars:[0x2928,0xffff]});
  const events=[];
  const probe={WorkspaceFontFallbackTake:()=>{const p=pending;pending='';return p;},
    WorkspaceFont:(bytes,config)=>events.push(['font',bytes,config]),
    WorkspaceFontFallbackComplete:value=>events.push(['complete',JSON.parse(value)]),
    WorkspaceFontFallbackRetry:()=>{events.push(['retry']);pending=JSON.stringify({id:2,family:'UI',scalars:[0x2928,0xffff]});}};
  let retry;
  const loader=fallbackFonts(probe,()=>events.push(['wake']),fetcher,(message,action)=>{retry=action;});
  return {loader,events,retry:()=>retry()};
}
test('indexed fallback publishes files before coverage without per-character POSTs',async()=>{
  const urls=[];const f=fixture(async url=>{urls.push(url);return response();});
  await f.loader.frame();await f.loader.frame();
  assert.deepEqual(urls,['/host/fonts/catalog?family=UI','/host/fonts/files/'+hash]);
  assert.equal(f.events[0][0],'font');assert.equal(f.events[1][0],'complete');
  assert.equal(f.events[1][1].assignments[10536],hash.toUpperCase());
  assert.equal(f.events[1][1].assignments[65535],null);
});
test('disposed delivery cannot publish and failure retries explicitly',async()=>{
  let complete;
  const f=fixture(()=>new Promise(resolve=>{complete=resolve;}));
  const pending=f.loader.frame();await new Promise(setImmediate);f.loader.dispose();complete(response());await pending;
  assert.deepEqual(f.events,[]);
  let fail=true;
  const g=fixture(async()=>fail?{ok:false}:response());
  await g.loader.frame();await g.loader.frame();fail=false;g.retry();await g.loader.frame();
  assert.equal(g.events.at(-2)[0],'complete');
});
test('coverage uses sorted inclusive ranges and preserves host preference',async()=>{
  assert.equal(covers([[65,90],[100,110]],90),true);assert.equal(covers([[65,90],[100,110]],91),false);
  let calls=0;const events=[];
  const fonts=new HostFonts({WorkspaceFont:()=>events.push('install')},async()=>{calls++;return response();},null);
  await fonts.warm(['UI']);const after=calls;
  assert.deepEqual(await fonts.resolve('UI',[65,0x2928,0x10ffff]),{65:hash.toUpperCase(),10536:hash.toUpperCase(),1114111:null});
  assert.equal(calls,after);assert.equal(events.length,1);
});
test('verified files survive a new runtime and corrupt cache entries refetch',async()=>{
  const entries=new Map();let downloads=0;
  const cache={match:async key=>entries.get(key)?.clone(),put:async(key,value)=>entries.set(key,value.clone()),
    keys:async()=>[...entries.keys()],delete:async key=>entries.delete(key)};
  const storage={open:async()=>cache};
  const request=async()=>{downloads++;return response();};
  const first=new HostFonts({},request,storage);assert.deepEqual(await first.file(hash),bytes);
  const second=new HostFonts({},request,storage);assert.deepEqual(await second.file(hash),bytes);assert.equal(downloads,1);
  entries.set('/host/fonts/files/'+hash,new Response('corrupt'));
  assert.deepEqual(await new HostFonts({},request,storage).file(hash),bytes);assert.equal(downloads,2);
});
test('browser fetch is not invoked with the font service as its receiver',async()=>{
  const fonts=new HostFonts({},function(){assert.equal(this,undefined);return Promise.resolve(response());},null);
  await fonts.catalog('UI');await fonts.file(hash);
});
test('selection and coverage share a queue through body consumption',async()=>{
  let release;const calls=[];
  const fetcher=async name=>{calls.push(name);return {ok:true,json:()=>name==='selection'?new Promise(resolve=>{release=resolve;}):Promise.resolve({})};};
  const a=fontRequests(fetcher),b=fontRequests(fetcher);
  const first=a('selection'),second=b('coverage');await new Promise(setImmediate);
  assert.deepEqual(calls,['selection']);release({});await first;await second;
  assert.deepEqual(calls,['selection','coverage']);
});
