// Run the production loader against host/managed doubles; no engine or browser.
import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {loadStartupAssemblies} from '../../runtime/wwwroot/startup-assemblies.js';
import {renderGui} from '../../runtime/wwwroot/gui.js';

const source = readFileSync(new URL('../../runtime/wwwroot/main.js', import.meta.url), 'utf8')
  .replace(/^import .*;\n/gm, '')
  .replace(/import\(([^)]+)\)/g, 'Promise.resolve(modules[$1])');
const AsyncFunction = Object.getPrototypeOf(async function(){}).constructor;
const run = new AsyncFunction('dotnet', 'workspaceViewport', 'workspaceParameters', 'fetch',
  'window', 'document', 'location', 'requestAnimationFrame', 'modules', 'console', 'loadStartupAssemblies', 'yieldStartupPaint', source);

async function start({query='', failAt='', incomplete=false, assetHeaders=null, reports={},render=()=>{},warm=async()=>false}={}) {
  const calls=[];
  const state={checks:[{ok:true}],loaded:true};
  const result={textContent:''};
  const window={__loading:{status(){},finish(){calls.push('finish');}}};
  const document={body:{classList:{add(){},remove(){}},prepend(){},style:{setProperty(){}}},
    documentElement:{style:{setProperty(){}}},querySelector:()=>result,getElementById:()=>result,
    createElement:()=>({})};
  const values={StartEntry:state,EntryFrame:state,InitializeEco:{checks:[]},
    WorkspaceFrame:[],WorkspaceUpdate:{commands:[]},WorkspaceFontSnapshot:{},GameSnapshot:{},WorkspaceState:{},...reports};
  const managed=new Proxy({}, {get(_,name){
    return (...args)=>{
      calls.push([name,...args]);
      if(name===failAt) throw new Error('injected '+name);
      if(name in values) return JSON.stringify(typeof values[name]==='function'?values[name](...args):values[name]);
      return 1;
    };
  }});
  const dotnet={withDebugging(){return this;},async create(){calls.push('create');
    return {getAssemblyExports:async()=>({Probe:managed})};}};
  const fetch=async path=>({ok:true,headers:new Headers(assetHeaders ?? {'x-slop-decoded-length':incomplete?'2':'1'}),
    arrayBuffer:async()=>new Uint8Array([1]).buffer,
    json:async()=>path==='./game/manifest.json'?[{file:'Game.dll'}]:[],
    text:async()=>'{}',
    body:(async function*(){yield new Uint8Array([1]);})()});
  const modules={
    './asset-archive.js':{openAssetArchive:async url=>({response:await fetch(url),stored:Promise.resolve()})},
    './gui.js':{renderGui:render,renderGuiProbe:()=>({})},
    './workspace.js':{startWorkspace(){calls.push('live');return {};}},
    './workspace-rasterizer.js':{installLocalRasterizer:async()=>{}},
    './font-delivery.js':{warmWorkspaceFonts:warm},
    './workspace-fonts.js':{dynamicFonts:()=>async()=>{}}
  };
  // Keep host font catalogs empty and preferences disabled; exercise the actual
  // assembly, archive, Entry and presentation ordering without a browser.
  await run(dotnet,()=>({width:1280,height:720,uiScale:1}),()=>new URLSearchParams('localPreferences=off&'+query),
    fetch,window,document,{origin:'http://localhost'},callback=>callback(),modules,{log(){}},
    (manifest,load)=>loadStartupAssemblies(manifest,load,fetch),async()=>{calls.push('paint');});
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

for(const stage of ['initial','later'])test(`${stage} startup font redraw preserves image uploads from the preceding packet`,async()=>{
  const previousDocument=globalThis.document,previousImageData=globalThis.ImageData;
  const context=new Proxy({}, {get:(_,method)=>{
    if(method==='getTransform')return ()=>({a:1,b:0,c:0,d:1,e:0,f:0});
    return ()=>{};
  }});
  const canvas=()=>({width:1280,height:720,getContext:()=>context});
  globalThis.document={createElement:canvas};
  globalThis.ImageData=class {};
  const target=canvas(),id=stage==='initial'?-127:-128;
  const upload={kind:'image',value:{id,width:1,height:1,pixels:'/////w=='}};
  const draw={kind:'imageBatch',value:{id,color:[1,1,1,1],quads:[0,0,1,1,0,0,1,1]}};
  const emitted=[],rendered=[];
  let updates=0,warmups=0;
  const packet=commands=>{emitted.push(commands);return commands;};
  try {
    const {calls,result}=await start({query:'nativeFonts',
      warm:async()=>++warmups===(stage==='initial'?2:3),
      reports:{
        WorkspaceFrame:()=>packet(stage==='initial'?[upload,draw]:[]),
        WorkspaceUpdate:()=>({commands:packet(stage==='later' && updates++===0?[upload,draw]:[draw])})
      },
      // Use the real resource consumer so a lost upload reproduces the reported
      // Missing GUI image error, rather than just checking mocked call counts.
      render:(_canvas,commands)=>{renderGui(target,commands);rendered.push(commands);}
    });
    assert.equal(result.error,undefined);
    assert.ok(calls.includes('live'));
    assert.equal(emitted.length,32);
    assert.deepEqual(rendered,emitted);
  } finally {
    renderGui(target,[{kind:'releaseImage',value:id}]);
    globalThis.document=previousDocument;globalThis.ImageData=previousImageData;
  }
});

test('colony actions are rejected before creating a runtime',async()=>{
  for(const query of ['play','load','save']) {
    const {calls,result}=await start({query});
    assert.match(result.error,/cannot create or load a colony/);
    assert.equal(calls.includes('create'),false);
  }
});

test('archive chunks and synchronous startup stages admit painting before managed work',async()=>{
  const {calls,result}=await start();
  assert.equal(result.error,undefined);
  for(const name of ['AssetChunk','InstallCore','StartEntry','EntryFrame','InstallModContent','InitializeEco','WorkspaceFrame','WorkspaceUpdate']) {
    const indices=calls.flatMap((call,index)=>Array.isArray(call)&&call[0]===name?[index]:[]);
    assert.ok(indices.length>0,name);
    for(const index of indices)assert.equal(calls[index-1],'paint',name);
  }
});

test('incomplete archives abort upload before Entry and report failure',async()=>{
  const {calls,result}=await start({incomplete:true});
  assert.match(result.error,/Incomplete asset transfer/);
  assert.deepEqual(calls.filter(c=>c[0]==='EndAssetUpload'),[['EndAssetUpload',false]]);
  assert.equal(calls.some(c=>c[0]==='StartEntry'),false);
});

test('compressed archives validate decoded size without a transport length',async()=>{
  for(const contentLength of [null,'20']) {
    const assetHeaders={'content-encoding':'gzip','x-slop-decoded-length':'1'};
    if(contentLength!==null)assetHeaders['content-length']=contentLength;
    const {calls,result}=await start({assetHeaders});
    assert.equal(result.error,undefined);
    assert.deepEqual(calls.filter(c=>c[0]==='EndAssetUpload'),[['EndAssetUpload',true]]);
  }
});

test('missing decoded archive size aborts instead of trusting compressed length',async()=>{
  const {calls,result}=await start({assetHeaders:{'content-length':'1','content-encoding':'gzip'}});
  assert.match(result.error,/Incomplete asset transfer/);
  assert.deepEqual(calls.filter(c=>c[0]==='EndAssetUpload'),[['EndAssetUpload',false]]);
});

test('failed workspace initialization releases Entry and never starts the live loop',async()=>{
  const {calls,result,message}=await start({failAt:'WorkspaceFrame'});
  assert.match(result.error,/injected WorkspaceFrame/);
  assert.equal(calls.filter(c=>c[0]==='StopEntry').length,1);
  assert.equal(calls.includes('live'),false);
  assert.match(message,/Workspace could not start/);
});

test('reported bootstrap, frame and mod failures stop startup and preserve the cause',async()=>{
  for (const name of ['StartEntry','EntryFrame','InitializeEco']) {
    const report={checks:[{name:'native stage',ok:false,errors:['native failure']}],loaded:false};
    const {calls,result,message}=await start({reports:{[name]:report}});
    assert.match(result.error,/native stage: native failure/);
    assert.match(message,/Workspace could not start/);
    assert.equal(calls.filter(c=>c[0]==='StopEntry').length,1);
    assert.equal(calls.filter(c=>c==='finish').length,1);
    assert.equal(calls.some(c=>c[0]==='WorkspaceFrame'),false);
    assert.equal(calls.includes('live'),false);
    if(name==='StartEntry') assert.equal(calls.some(c=>c[0]==='EntryFrame'),false);
    if(name==='EntryFrame') assert.equal(calls.filter(c=>c[0]==='EntryFrame').length,1);
  }
});

test('unfinished Entry loading fails visibly and releases the engine',async()=>{
  const report={checks:[{ok:true}],loaded:false};
  const {calls,result,message}=await start({reports:{StartEntry:report,EntryFrame:report}});
  assert.match(result.error,/Entry did not finish loading/);
  assert.match(message,/Workspace could not start/);
  assert.equal(calls.filter(c=>c[0]==='EntryFrame').length,5);
  assert.equal(calls.filter(c=>c[0]==='StopEntry').length,1);
  assert.equal(calls.filter(c=>c==='finish').length,1);
  assert.equal(calls.some(c=>c[0]==='InitializeEco'),false);
  assert.equal(calls.includes('live'),false);
});
