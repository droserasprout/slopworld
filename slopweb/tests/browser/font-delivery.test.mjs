import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {readFileSync} from 'node:fs';
import {fallbackFonts,fontRequests} from '../../runtime/wwwroot/font-delivery.js';
import {HostFonts,covers} from '../../runtime/wwwroot/host-fonts.js';
const bytes=new TextEncoder().encode('font');
const hash=createHash('sha256').update(bytes).digest('hex');
const catalog={version:1,revision:'v1',faces:[{hash,family:'Fallback',loaderFlags:513,coverage:[[65,90],[0x2928,0x2928]]}],order:[hash],preload:[hash]};
const response=()=>({ok:true,json:async()=>catalog,arrayBuffer:async()=>bytes.buffer});
test('glyph-only startup completion requests a redraw even without host fallback work',async()=>{
  const source=readFileSync(new URL('../../runtime/wwwroot/font-delivery.js',import.meta.url),'utf8')
    .replace(/^import .*;\n/gm,'').replaceAll('export ','');
  let changed=true,warmed=0;
  const warm=new Function('hostFonts','settleWorkspaceGlyphs',source+'\nreturn warmWorkspaceFonts;')(
    ()=>({warm:async()=>{warmed++;}}),async()=>changed);
  const probe={WorkspaceFontFallbackTake:()=>''};
  assert.equal(await warm(probe),true);
  changed=false;assert.equal(await warm(probe),false);
  assert.equal(warmed,2);
});
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

test('concurrent fallback installs share download and publication through asynchronous handoff',{timeout:5000},async()=>{
  let release,started;
  const gate=new Promise(resolve=>{release=resolve;}),entered=new Promise(resolve=>{started=resolve;});
  let downloads=0,installs=0;
  const fonts=new HostFonts({WorkspaceFont:()=>{installs++;started();return gate;}},async()=>{downloads++;return response();},null);
  const face=catalog.faces[0];
  const a=fonts.install(face),b=fonts.install(face);
  await entered;
  const c=fonts.install(face);
  assert.equal(downloads,1);assert.equal(installs,1);
  release();await Promise.all([a,b,c]);
  await fonts.install(face);assert.equal(downloads,1);assert.equal(installs,1);
});

test('failed shared installation is retryable and disposal blocks late publication',async()=>{
  let fail=true,installs=0;
  const fonts=new HostFonts({WorkspaceFont:()=>{installs++;if(fail)throw Error('install');}},async()=>response(),null);
  const face=catalog.faces[0];
  const results=await Promise.allSettled([fonts.install(face),fonts.install(face)]);
  assert.deepEqual(results.map(result=>result.status),['rejected','rejected']);assert.equal(installs,1);
  fail=false;await fonts.install(face);assert.equal(installs,2);
  let release;
  const stopped=new HostFonts({WorkspaceFont:()=>assert.fail('late publication')},()=>new Promise(resolve=>{release=resolve;}),null);
  const work=stopped.install(face);await new Promise(setImmediate);
  stopped.dispose();release(response());await work;
  assert.equal(stopped.installed.size,0);
});

test('font warming overlaps two families with one shared fallback installation',{timeout:5000},async()=>{
  const families=[],releases=new Map();let downloads=0,installs=0;
  let admitted;const third=new Promise(resolve=>{admitted=resolve;});
  const fonts=new HostFonts({WorkspaceFont:()=>{installs++;}},async url=>{
    if(url.includes('catalog')) {
      const family=new URL(url,'http://localhost').searchParams.get('family');families.push(family);
      return new Promise(resolve=>{releases.set(family,()=>resolve(response()));if(family==='Other')admitted();});
    }
    downloads++;return response();
  },null);
  const work=fonts.warm(['UI','Terminal','UI','Other']);
  assert.deepEqual(families,['UI','Terminal']);
  releases.get('Terminal')();releases.get('UI')();
  await third;
  assert.deepEqual(families,['UI','Terminal','Other']);
  assert.equal(downloads,1);assert.equal(installs,1);
  releases.get('Other')();await work;
  assert.equal(downloads,1);assert.equal(installs,1);
});

test('failed warming stops new families and settles active work before reporting failure',{timeout:5000},async()=>{
  let release;const gate=new Promise(resolve=>{release=resolve;});
  const families=[];
  const fonts=new HostFonts({},async url=>{
    const family=new URL(url,'http://localhost').searchParams.get('family');families.push(family);
    if(family==='UI')throw Error('catalog failed');
    await gate;return {ok:true,json:async()=>({...catalog,preload:[]})};
  },null);
  let settled=false;
  const work=fonts.warm(['UI','Terminal','Other']).finally(()=>{settled=true;});
  const failure=assert.rejects(work,/catalog failed/);
  await new Promise(setImmediate);
  assert.equal(settled,false);assert.deepEqual(families,['UI','Terminal']);
  release();await failure;assert.deepEqual(families,['UI','Terminal']);
});

test('a failed preload waits for its sibling font handoff before warming rejects',{timeout:5000},async()=>{
  const otherBytes=new TextEncoder().encode('other font');
  const otherHash=createHash('sha256').update(otherBytes).digest('hex');
  let release,entered;
  const gate=new Promise(resolve=>{release=resolve;}),started=new Promise(resolve=>{entered=resolve;});
  let installs=0,settled=false;
  const fonts=new HostFonts({WorkspaceFont:()=>{installs++;}},async url=>{
    if(url.includes('catalog'))return {ok:true,json:async()=>({...catalog,
      faces:[...catalog.faces,{...catalog.faces[0],hash:otherHash}],preload:[hash,otherHash]})};
    if(url.endsWith(hash))throw Error('first font failed');
    entered();await gate;
    return {ok:true,arrayBuffer:async()=>otherBytes.buffer};
  },null);
  const work=fonts.warm(['UI']).finally(()=>{settled=true;});
  const failure=assert.rejects(work,/first font failed/);
  await started;await new Promise(setImmediate);
  assert.equal(settled,false);assert.equal(installs,0);
  release();await failure;assert.equal(installs,1);
});
