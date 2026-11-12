import {test} from 'node:test';
import assert from 'node:assert/strict';
import {fallbackFonts,fontRequests} from '../../runtime/wwwroot/font-delivery.js';

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
const response=()=>({ok:true,json:async()=>({fonts:{'face0.ttf':btoa('font')},faces:[{font:'face0.ttf',bake:'{}'}],assignments:{10536:'face0.ttf',65535:null}})});

test('fallback files install before coverage, missing coverage stays explicit, requests coalesce',async()=>{
  let complete,calls=0;
  const f=fixture(async(url,options)=>{calls++;assert.deepEqual(JSON.parse(options.body),{fallback:{family:'UI',scalars:[10536,65535]}});return new Promise(resolve=>{complete=resolve;});});
  const pending=f.loader.frame();await f.loader.frame();await new Promise(setImmediate);
  complete(response());await pending;
  assert.equal(calls,1);assert.equal(f.events[0][0],'font');assert.equal(f.events[1][0],'complete');
  assert.match(f.events[1][1].assignments[10536],/^[A-F0-9]{64}$/);assert.equal(f.events[1][1].assignments[65535],null);
  assert.equal(f.events[2][0],'wake');
});

test('disposed delivery cannot publish and failed delivery retries explicitly',async()=>{
  let complete;
  const f=fixture(()=>new Promise(resolve=>{complete=resolve;}));
  const pending=f.loader.frame();await new Promise(setImmediate);f.loader.dispose();complete(response());await pending;
  assert.deepEqual(f.events,[]);
  let calls=0;
  const g=fixture(async()=>++calls===1?{ok:false}:response());
  await g.loader.frame();await g.loader.frame();assert.equal(calls,1);
  g.retry();await g.loader.frame();assert.equal(calls,2);assert.equal(g.events.at(-2)[0],'complete');
});

test('selection and coverage share a queue through body consumption',async()=>{
  let release;const calls=[];
  const fetcher=async name=>{calls.push(name);return {ok:true,json:()=>name==='selection'?new Promise(resolve=>{release=resolve;}):Promise.resolve({})};};
  const a=fontRequests(fetcher),b=fontRequests(fetcher);
  const first=a('selection'),second=b('coverage');await new Promise(setImmediate);
  assert.deepEqual(calls,['selection']);release({});await first;await second;
  assert.deepEqual(calls,['selection','coverage']);
});
