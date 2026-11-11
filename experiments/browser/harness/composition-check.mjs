// Exercise Chromium composition in disposable terminals, through native input.
import assert from 'node:assert/strict';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';

const [profile,reload='0']=process.argv.slice(2);
assert.ok(['0','1'].includes(reload),'Reload must be 0 or 1');
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
const report={checks:[],cleanup:false};
let installed=false,session;
const owned=async()=>assert.equal(await evaluate('JSON.parse(window.__managedProbe.WorkspaceState()).session'),session,'Input target changed');
const contains=text=>evaluate(`window.__managedProbe.WorkspaceTerminalContains(${JSON.stringify(session)},${JSON.stringify(text)})`);
const key=async(key,code,windowsVirtualKeyCode)=>{
  await owned();
  for(const type of ['keyDown','keyUp'])await call('Input.dispatchKeyEvent',{type,key,code,windowsVirtualKeyCode});
};
const insert=async text=>{await owned();await call('Input.insertText',{text});};
const compose=async text=>{
  await owned();
  await call('Input.imeSetComposition',{text,selectionStart:text.length,selectionEnd:text.length});
};
try {
  await until(()=>evaluate("window.__workspace?.status==='running'"),'workspace startup',180000);
  if(reload==='1') {
    const origin=await evaluate('performance.timeOrigin');
    await call('Page.reload',{ignoreCache:true});
    await until(()=>evaluate(`performance.timeOrigin!==${origin} && window.__workspace?.status==='running'`),'reloaded workspace',180000);
  }
  await call('Page.bringToFront');
  await call('Emulation.setFocusEmulationEnabled',{enabled:true});
  await evaluate(await readFile(new URL('./benchmark-workload.js',import.meta.url),'utf8'));
  installed=true;
  const fixture=fileURLToPath(new URL('./benchmark-fixture.py',import.meta.url));
  const command="python3 -u '"+fixture.replaceAll("'","'\\''")+"'";
  report.fixture=await evaluate(`window.__benchmark.prepare(${JSON.stringify(command)})`);
  session=report.fixture.created[0];
  console.log(JSON.stringify(report.fixture));
  await evaluate(`(() => {
    const canvas=document.querySelector('canvas');
    window.__compositionEvents=[];
    window.__compositionAbort=new AbortController();
    for(const type of ['compositionstart','compositionupdate','compositionend','beforeinput','input'])
      document.addEventListener(type,e=>window.__compositionEvents.push({type:e.type,data:e.data,inputType:e.inputType,trusted:e.isTrusted}),{capture:true,signal:window.__compositionAbort.signal});
    canvas.focus();
  })()`);
  const marker='IME_'+Date.now()+'_';
  // A sentinel after the committed text makes duplicate insertion fail the echo
  // match. Native receive-buffer observation does not depend on font coverage.
  await insert('echo '+marker);
  await compose('か');
  await compose('漢字🧪');
  await key('Process','Enter',229);
  await pause(400);
  assert.equal(await contains('ACK_'+marker),false,'Composition key submitted the terminal line');
  await insert('漢字🧪');
  await insert('_END');
  await key('Enter','Enter',13);
  await until(()=>contains('ACK_'+marker+'漢字🧪_END'),'exact composed terminal echo');
  report.checks.push('updated preedit, supplementary character and one exact commit');
  const canceled=marker+'CANCEL_';
  await insert('echo '+canceled);
  await compose('discard_me');
  await compose('');
  await insert('KEPT_END');
  await key('Enter','Enter',13);
  await until(()=>contains('ACK_'+canceled+'KEPT_END'),'canceled preedit stays out of terminal');
  report.checks.push('canceled composition emits no terminal text');
  report.events=await evaluate('window.__compositionEvents');
  for(const type of ['compositionstart','compositionupdate','compositionend'])
    assert.ok(report.events.some(e=>e.type===type&&e.trusted),'Missing trusted '+type);
  report.checks.push('Chromium delivered trusted composition events');
} catch(error) {
  report.error=String(error);throw error;
} finally {
  try {
    // End preedit while the owned pane is still selected, before restoring any
    // existing agent. Never let a later focus change commit test text there.
    if(session && await evaluate('JSON.parse(window.__managedProbe.WorkspaceState()).session')===session)
      await call('Input.imeSetComposition',{text:'',selectionStart:0,selectionEnd:0});
    report.events=await evaluate('window.__compositionEvents ?? []');
    await evaluate('window.__compositionAbort?.abort();delete window.__compositionAbort;delete window.__compositionEvents;');
    if(installed){await evaluate('window.__benchmark.cleanup()');report.cleanup=true;}
  } catch(error) {report.cleanupError=String(error);throw error;}
  finally {
    await mkdir(new URL('../artifacts/',import.meta.url),{recursive:true});
    await writeFile(new URL('../artifacts/composition-check.json',import.meta.url),JSON.stringify(report,null,2)+'\n');
    console.log(JSON.stringify(report));
    for(const entry of pending.values())clearTimeout(entry.timer);
    socket.close();
  }
}
