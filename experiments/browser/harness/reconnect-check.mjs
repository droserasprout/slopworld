// Interrupt only the retained diagnostic page; the host daemon stays available.
import assert from 'node:assert/strict';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';

const [profile]=process.argv.slice(2);
const port=(await readFile(`${profile}/DevToolsActivePort`,'utf8')).split('\n')[0];
const targets=await(await fetch(`http://127.0.0.1:${port}/json/list`)).json();
const page=targets.find(t=>t.type==='page'&&t.url.startsWith('http://127.0.0.1:'));
assert.ok(page,'No diagnostic workspace');
const socket=new WebSocket(page.webSocketDebuggerUrl);
await new Promise((resolve,reject)=>{socket.onopen=resolve;socket.onerror=reject;});
let serial=0;
const pending=new Map();
const call=(method,params={})=>new Promise((resolve,reject)=>{
  if(socket.readyState!==WebSocket.OPEN) {reject(new Error('Diagnostic browser closed'));return;}
  const id=++serial,timer=setTimeout(()=>{pending.delete(id);reject(new Error(method+' timed out'));},120000);
  pending.set(id,{resolve,reject,timer});socket.send(JSON.stringify({id,method,params}));
});
socket.onmessage=({data})=>{
  const message=JSON.parse(data),entry=pending.get(message.id);
  if(!entry)return;
  pending.delete(message.id);clearTimeout(entry.timer);
  message.error?entry.reject(new Error(JSON.stringify(message.error))):entry.resolve(message.result);
};
socket.onclose=()=>{
  for(const entry of pending.values()) {
    clearTimeout(entry.timer);entry.reject(new Error('Diagnostic browser closed'));
  }
  pending.clear();
};
const evaluate=async expression=>{
  const result=await call('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});
  if(result.exceptionDetails)throw new Error(JSON.stringify(result.exceptionDetails));
  return result.result.value;
};
const pause=ms=>new Promise(resolve=>setTimeout(resolve,ms));
const until=async(condition,description,timeout=30000)=>{
  const end=Date.now()+timeout;
  while(!await condition()) {
    if(Date.now()>end)throw new Error('Timeout: '+description);
    await pause(100);
  }
};
const network=offline=>call('Network.emulateNetworkConditions',{
  offline,latency:0,downloadThroughput:-1,uploadThroughput:-1,
});
let installed=false;
let networkChanged=false;
let scriptId;
const report={cycles:[],cleanup:false};
try {
  await until(()=>evaluate("window.__workspace?.status==='running'"),'workspace startup');
  console.log('Workspace ready');
  await call('Page.enable');
  // Track only this page's transport constructors. Heap-wide Runtime.queryObjects
  // can stall large WASM pages during garbage collection, so do not use it here.
  const injection=await call('Page.addScriptToEvaluateOnNewDocument',{source:`(() => {
    const original=window.WebSocket,sockets=new Set();
    window.__slopReconnectCheck={original,sockets};
    window.WebSocket=class extends original {
      constructor(...args) {
        super(...args);
        if(this.url===location.origin.replace('http:','ws:')+'/host/ws') {
          sockets.add(this);this.addEventListener('close',()=>sockets.delete(this),{once:true});
        }
      }
    };
  })();`});
  scriptId=injection.identifier;
  const origin=await evaluate('performance.timeOrigin');
  await call('Page.reload',{ignoreCache:true});
  await until(()=>evaluate(`performance.timeOrigin!==${origin} && window.__workspace?.status==='running'`),'instrumented startup',180000);
  assert.equal(await evaluate('window.__slopReconnectCheck?.sockets.size'),1,'Transport tracking was not installed');
  await call('Network.enable');
  await evaluate(await readFile(new URL('./benchmark-workload.js',import.meta.url),'utf8'));
  installed=true;
  const fixture=fileURLToPath(new URL('./benchmark-fixture.py',import.meta.url));
  const command="python3 -u '"+fixture.replaceAll("'","'\\''")+"'";
  report.fixture=await evaluate(`window.__benchmark.prepare(${JSON.stringify(command)})`);
  console.log(JSON.stringify(report.fixture));
  for(let cycle=0;cycle<2;cycle++) {
    console.log('Interrupting connection: '+cycle);
    assert.equal(await evaluate('JSON.parse(window.__managedProbe.WorkspaceBrowseState()).online'),true);
    // Offline emulation alone need not terminate existing WebSockets. Close the
    // real page-owned transport too, without adding production diagnostic hooks.
    networkChanged=true;
    await network(true);
    const closed=await evaluate(`(() => {
        const sockets=[...window.__slopReconnectCheck.sockets].filter(s=>s.readyState===WebSocket.OPEN);
        for(const s of sockets)s.close(1000,'diagnostic reconnect');
        return sockets.length;
      })()`);
    await until(async()=>!await evaluate('JSON.parse(window.__managedProbe.WorkspaceBrowseState()).online'),'native hub offline');
    console.log('Native hub offline');
    const httpBlocked=await evaluate("fetch('/host/health').then(()=>false,()=>true)");
    assert.equal(httpBlocked,true,'Page HTTP stayed available during interruption');
    const frames=await evaluate('window.__workspace.frames');
    await pause(1500);
    assert.ok(await evaluate('window.__workspace.frames')>frames,'Offline UI stopped updating');
    await network(false);
    await until(()=>evaluate('JSON.parse(window.__managedProbe.WorkspaceBrowseState()).online'),'native hub reconnect');
    console.log('Native hub reconnected');
    // Unique reset acknowledgment and real DOM echo require the recovered socket,
    // native subscription, input queue and actual tmux process to work together.
    await evaluate('window.__benchmark.reset()');
    const typing=await evaluate("window.__benchmark.run('typing')");
    assert.equal(typing.latencyMs.length,20);
    assert.equal(typing.game.hasGame,false);
    report.cycles.push({closedSockets:closed,httpBlocked,offlineFramesAdvanced:true,reconnected:true,echoes:typing.latencyMs.length});
  }
} catch(error) {
  report.error=String(error);
  console.error('Reconnect check failed: '+error);
  throw error;
} finally {
  try {
    if(networkChanged)await network(false);
    if(installed) {
      await until(()=>evaluate('JSON.parse(window.__managedProbe.WorkspaceBrowseState()).online'),'cleanup reconnect');
      await evaluate('window.__benchmark.cleanup()');report.cleanup=true;
    }
    if(scriptId) {
      await call('Page.removeScriptToEvaluateOnNewDocument',{identifier:scriptId});
      await evaluate('if(window.__slopReconnectCheck){window.WebSocket=window.__slopReconnectCheck.original;delete window.__slopReconnectCheck;}');
    }
    console.log(JSON.stringify(report));
  } catch(error) {
    report.cleanupError=String(error);
    throw error;
  } finally {
    await mkdir(new URL('../artifacts/',import.meta.url),{recursive:true});
    await writeFile(new URL('../artifacts/reconnect-check.json',import.meta.url),JSON.stringify(report,null,2)+'\n');
    for(const entry of pending.values())clearTimeout(entry.timer);
    socket.close();
  }
}
