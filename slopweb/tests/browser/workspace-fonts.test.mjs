import test from 'node:test';
import assert from 'node:assert/strict';
import {dynamicFonts} from '../../runtime/wwwroot/workspace-fonts.js';

test('settings bursts coalesce, stale font data never publishes, failures do not loop', async () => {
  const requests=[], published=[];
  let resolve;
  const probe={WorkspaceFont:(...args)=>published.push(args),WorkspaceFontChains:()=>{},WorkspaceFontsReady:()=>published.push('ready')};
  const change=dynamicFonts(probe, (url, options)=> {
    requests.push(JSON.parse(options.body));
    return new Promise(done=>{resolve=done;});
  }, ()=>5/3);
  const first=change('{}');
  await change('{}');
  await change('{"fontName":"new"}');
  await new Promise(setImmediate);
  resolve({ok:true,json:async()=>({fonts:{},faces:[],chains:{}})});
  await new Promise(setImmediate);
  assert.equal(requests.length,2);
  assert.equal(published.length,0);
  assert.equal(requests[1].density,undefined); // Density is handled synchronously in WASM.
  resolve({ok:true,json:async()=>({fonts:{'a':btoa('font')},faces:[{font:'a',bake:'{"atlas":{"width":1,"height":1}}'}],chains:{}})});
  await first;
  assert.equal(published.length,2);
  await change('{"fontName":"new","theme":"unrelated"}');
  assert.equal(requests.length,2);
});

test('failed selection remains bounded and does not retry on preference writes', async () => {
  let requests=0;
  const change=dynamicFonts({}, async()=>{requests++;return {ok:false};});
  await change('{}'); await change('{}');
  assert.equal(requests,1);
});

test('A to B to A restores cached chains; in-flight A to B to A publishes A once', async () => {
  const chains=[], requests=[];
  const probe={WorkspaceFont:()=>{},WorkspaceFontChains:json=>chains.push(JSON.parse(json)),WorkspaceFontsReady:()=>{}};
  const response = family => ({ok:true,json:async()=>({fonts:{},faces:[],chains:{family}})});
  const change=dynamicFonts(probe, async(url, options)=>{const family=JSON.parse(options.body).fontName;requests.push(family);return response(family);});
  await change('{"fontName":"A"}'); await change('{"fontName":"B"}'); await change('{"fontName":"A"}');
  assert.deepEqual(requests,['A','B']);
  assert.deepEqual(chains.map(c=>c.family),['A','B','A']);
  let release;
  const pending=dynamicFonts(probe, ()=>new Promise(done=>{release=done;}));
  const first=pending('{"fontName":"A"}');
  await pending('{"fontName":"B"}'); await pending('{"fontName":"A"}');
  await new Promise(setImmediate);
  release(response('A')); await first;
  assert.equal(chains.at(-1).family,'A');
});

test('seeded startup needs no request and failures restore the usable selection', async () => {
  let calls=0, restored;
  const probe={WorkspaceFontRestore:json=>{restored=JSON.parse(json);}};
  const change=dynamicFonts(probe,async()=>{calls++;return {ok:false};},()=>1,()=>{}, {selection:'{}',chains:'{}'});
  await change('{}'); assert.equal(calls,0);
  await change('{"fontName":"missing"}');
  assert.equal(calls,1); assert.equal(restored.fontName,'monospace');
});

test('explicit retry recovers a failed selection and clears the visible error', async () => {
  let retry, calls=0, cleared=false;
  const probe={WorkspaceFontChains:()=>{},WorkspaceFontsReady:()=>{},WorkspaceFontRestore:()=>{}};
  const change=dynamicFonts(probe,async(url,options)=> {
    if (++calls===1) return {ok:false};
    assert.equal(options.headers['x-slop-font-retry'],'1');
    return {ok:true,json:async()=>({fonts:{},faces:[],chains:{}})};
  },()=>1,(message, action)=>{retry=action;if(!message)cleared=true;}, {selection:'{}',chains:'{}'});
  await change('{"fontName":"recover"}');
  retry(); await new Promise(setImmediate);
  assert.equal(calls,2); assert.equal(cleared,true);
});

test('pending font files hold the committed selection and publish only after loading', async () => {
  let complete;
  const held=[], committed=[];
  const a=JSON.stringify({fontName:'A'}), b=JSON.stringify({fontName:'B'});
  const probe={WorkspaceFontHold:value=>held.push(JSON.parse(value).fontName),
    WorkspaceFontRestore:value=>committed.push(JSON.parse(value).fontName),
    WorkspaceFontsReady:()=>{}, WorkspaceFontChains:()=>{}};
  const change=dynamicFonts(probe,()=>new Promise(resolve=>{complete=resolve;}),()=>1,()=>{}, {selection:a,chains:'{}'});
  const pending=change(b);
  assert.deepEqual(held,['A']); assert.deepEqual(committed,[]);
  await new Promise(setImmediate);
  complete({ok:true,json:async()=>({fonts:{},faces:[],chains:{B:['B']}})});
  await pending;
  assert.deepEqual(committed,['B']);
});

