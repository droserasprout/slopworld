// Synthetic body latency only: no game data, network, CLR, or browser execution.
import assert from 'node:assert/strict';
import {performance} from 'node:perf_hooks';
import {setTimeout} from 'node:timers/promises';
import {loadStartupAssemblies} from '../../../runtime/wwwroot/startup-assemblies.js';

const manifest=Array.from({length:24},(_,i)=>({file:`${i}.dll`}));
const delayMs=6;
const request=async url=>({ok:true,arrayBuffer:async()=>{
  await setTimeout(delayMs);
  return Uint8Array.of(Number(url.match(/(\d+)\.dll$/)[1])).buffer;
}});
async function serial() {
  const loaded=[];
  for(const item of manifest) {
    const response=await request(`./game/${item.file}`);
    loaded.push(new Uint8Array(await response.arrayBuffer())[0]);
  }
  return {loaded,loadErrors:[]};
}
const baseline=[],optimized=[];
for(let round=0;round<6;round++)for(const parallel of round%2?[true,false]:[false,true]) {
  const start=performance.now();
  const result=await (parallel?loadStartupAssemblies(manifest,bytes=>bytes[0],request):serial());
  (parallel?optimized:baseline).push(performance.now()-start);
  assert.deepEqual(result,{loaded:manifest.map((_,i)=>i),loadErrors:[]});
}
const median=values=>values.sort((a,b)=>a-b)[values.length>>1];
const before=median(baseline),after=median(optimized);
console.log(JSON.stringify({assemblies:manifest.length,injectedBodyDelayMs:delayMs,
  baselineMs:before,optimizedMs:after,speedup:before/after}));
