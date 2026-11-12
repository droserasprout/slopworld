// Run the production loader against host/managed doubles; no engine or browser.
import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const source = readFileSync(new URL('../../runtime/wwwroot/main.js', import.meta.url), 'utf8')
  .replace(/^import .*;\n/gm, '')
  .replace(/import\(([^)]+)\)/g, 'Promise.resolve(modules[$1])');
const AsyncFunction = Object.getPrototypeOf(async function(){}).constructor;
const run = new AsyncFunction('dotnet', 'workspaceViewport', 'workspaceParameters', 'fetch',
  'window', 'document', 'location', 'requestAnimationFrame', 'modules', 'console', source);

async function start({query='', failAt='', incomplete=false}={}) {
  const calls=[];
  const state={checks:[{ok:true}],loaded:true};
  const result={textContent:''};
  const window={__loading:{status(){},finish(){calls.push('finish');}}};
  const document={body:{classList:{add(){},remove(){}},prepend(){},style:{setProperty(){}}},
    documentElement:{style:{setProperty(){}}},querySelector:()=>result,getElementById:()=>result,
    createElement:()=>({})};
  const values={StartEntry:state,EntryFrame:state,InitializeEco:{checks:[]},
    WorkspaceFrame:[],WorkspaceUpdate:{commands:[]},WorkspaceFontSnapshot:{},GameSnapshot:{},WorkspaceState:{}};
  const managed=new Proxy({}, {get(_,name){
    return (...args)=>{
      calls.push([name,...args]);
      if(name===failAt) throw new Error('injected '+name);
      if(name in values) return JSON.stringify(values[name]);
      return 1;
    };
  }});
  const dotnet={withDebugging(){return this;},async create(){calls.push('create');
    return {getAssemblyExports:async()=>({Probe:managed})};}};
  const fetch=async path=>({ok:true,headers:{get:()=>incomplete?'2':'1'},
    arrayBuffer:async()=>new Uint8Array([1]).buffer,
    json:async()=>path.endsWith('manifest.json')?[{file:'Game.dll'}]:[],
    body:(async function*(){yield new Uint8Array([1]);})()});
  const modules={
    './gui.js':{renderGui(){},renderGuiProbe:()=>({})},
    './workspace.js':{startWorkspace(){calls.push('live');return {};}}
  };
  // Disable optional font/preferences I/O; exercise the actual assembly, archive,
  // Entry and presentation ordering with no alternative diagnostic code path.
  await run(dotnet,()=>({width:1280,height:720,uiScale:1}),()=>new URLSearchParams('localPreferences=off&'+query),
    fetch,window,document,{origin:'http://localhost'},callback=>callback(),modules,{log(){}});
  return {calls,result:window.__probeResult,message:result.textContent};
}

test('product startup enters the workspace without running compatibility or colony probes',async()=>{
  const {calls,result}=await start();
  assert.equal(result.error,undefined);
  assert.ok(calls.indexOf('live')>calls.findIndex(c=>c[0]==='InitializeEco'));
  assert.deepEqual(calls.filter(c=>c[0]==='EndAssetUpload'),[['EndAssetUpload',true]]);
  for(const name of ['Run','Deep','Jobs','RequestPlay','LoadGame','SaveGame','GuiControlProbe'])
    assert.equal(calls.some(c=>c[0]===name),false,name);
});

test('colony actions are rejected before creating a runtime',async()=>{
  for(const query of ['play','load','save']) {
    const {calls,result}=await start({query});
    assert.match(result.error,/cannot create or load a colony/);
    assert.equal(calls.includes('create'),false);
  }
});

test('incomplete archives abort upload before Entry and report failure',async()=>{
  const {calls,result}=await start({incomplete:true});
  assert.match(result.error,/Incomplete asset transfer/);
  assert.deepEqual(calls.filter(c=>c[0]==='EndAssetUpload'),[['EndAssetUpload',false]]);
  assert.equal(calls.some(c=>c[0]==='StartEntry'),false);
});

test('failed workspace initialization releases Entry and never starts the live loop',async()=>{
  const {calls,result,message}=await start({failAt:'WorkspaceFrame'});
  assert.match(result.error,/injected WorkspaceFrame/);
  assert.equal(calls.filter(c=>c[0]==='StopEntry').length,1);
  assert.equal(calls.includes('live'),false);
  assert.match(message,/Workspace could not start/);
});
