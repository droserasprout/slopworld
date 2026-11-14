import test from 'node:test';
import assert from 'node:assert/strict';
import {loadStartupAssemblies} from '../../runtime/wwwroot/startup-assemblies.js';

const manifest=Array.from({length:10},(_,i)=>({file:`${i}.dll`}));
const tick=()=>new Promise(setImmediate);
test('ready assembly bodies wait for paint admission before each synchronous CLR load',async()=>{
  const loaded=[];let release;
  const work=loadStartupAssemblies(manifest.slice(0,2),bytes=>{loaded.push(bytes[0]);return bytes[0];},
    async url=>({ok:true,arrayBuffer:async()=>Uint8Array.of(Number(url.match(/(\d+)\.dll$/)[1])).buffer}),
    ()=>new Promise(resolve=>{release=resolve;}));
  await tick();assert.deepEqual(loaded,[]);
  release();await tick();assert.deepEqual(loaded,[0]);
  release();await work;assert.deepEqual(loaded,[0,1]);
});
test('startup overlaps four bodies but preserves CLR order and bounds ready buffers',{timeout:5000},async()=>{
  const started=[],loaded=[],releases=new Map();
  const fetch=async url=>{
    const index=Number(url.match(/(\d+)\.dll$/)[1]);started.push(index);
    return {ok:true,arrayBuffer:()=>new Promise(resolve=>releases.set(index,()=>resolve(Uint8Array.of(index).buffer)))};
  };
  const work=loadStartupAssemblies(manifest,bytes=>{loaded.push(bytes[0]);return bytes[0];},fetch);
  await tick();assert.deepEqual(started,[0,1,2,3]);
  // Later files can finish, but cannot load or open more slots past a slow head.
  for(const index of [3,2,1])releases.get(index)();
  await tick();assert.deepEqual(loaded,[]);assert.deepEqual(started,[0,1,2,3]);
  releases.get(0)();await tick();
  assert.deepEqual(loaded,[0,1,2,3]);assert.deepEqual(started,[0,1,2,3,4,5,6,7]);
  for(const index of [7,6,5,4])releases.get(index)();
  await tick();assert.deepEqual(loaded,[0,1,2,3,4,5,6,7]);
  for(const index of [9,8])releases.get(index)();
  const result=await work;
  assert.deepEqual(result,{loaded:Array.from({length:10},(_,i)=>i),loadErrors:[]});
});

test('early fetch/body failures and CLR failures retain manifest-ordered reporting and continue',{timeout:5000},async()=>{
  let release;
  const gate=new Promise(resolve=>{release=resolve;});
  const fetch=async url=>{
    const index=Number(url.match(/(\d+)\.dll$/)[1]);
    if(index===0)await gate;
    if(index===1)throw Error('network');
    return {ok:index!==2,status:404,arrayBuffer:async()=>{
      if(index===3)throw Error('body');
      return Uint8Array.of(index).buffer;
    }};
  };
  const work=loadStartupAssemblies(manifest,bytes=>{
    if(bytes[0]===4)throw Error('CLR');
    return bytes[0];
  },fetch);
  await tick();release();
  const result=await work;
  assert.deepEqual(result.loaded,[0,5,6,7,8,9]);
  assert.deepEqual(result.loadErrors,[
    {file:'1.dll',error:'Error: network'},{file:'2.dll',error:'Error: 2.dll: HTTP 404'},
    {file:'3.dll',error:'Error: body'},{file:'4.dll',error:'Error: CLR'}
  ]);
});

test('empty and short manifests download and load each entry exactly once',async()=>{
  let requests=0;
  const fetch=async()=>{requests++;return {ok:true,arrayBuffer:async()=>Uint8Array.of(1).buffer};};
  assert.deepEqual(await loadStartupAssemblies([],()=>assert.fail(),fetch),{loaded:[],loadErrors:[]});
  assert.equal(requests,0);
  assert.deepEqual(await loadStartupAssemblies(manifest.slice(0,2),bytes=>bytes[0],fetch),{loaded:[1,1],loadErrors:[]});
  assert.equal(requests,2);
});
