// Run a local diagnostic script in an already-loaded local browser workspace.
import { readFile } from 'node:fs/promises';
const [profile, script] = process.argv.slice(2);
const port = (await readFile(`${profile}/DevToolsActivePort`, 'utf8')).split('\n')[0];
const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
const page = targets.find(t => t.type === 'page' && t.url.startsWith('http://127.0.0.1:'));
if (!page) throw new Error('No live local workspace page');
const socket = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
let serial=0; const pending=new Map();
const call=(method,params={})=>new Promise((resolve,reject)=>{
  const id=++serial;
  const timer=setTimeout(()=>{pending.delete(id);reject(new Error('Browser evaluation timed out; runtime may still be working'));},Number(process.env.PROBE_EVALUATE_TIMEOUT_MS??30000));
  pending.set(id,{resolve,reject,timer});socket.send(JSON.stringify({id,method,params}));
});
socket.onmessage=({data})=>{
  const message=JSON.parse(data), entry=pending.get(message.id);
  if(entry){pending.delete(message.id);clearTimeout(entry.timer);if(message.error)entry.reject(new Error(JSON.stringify(message.error)));else entry.resolve(message.result);}
  if(message.method==='Runtime.bindingCalled' && message.params.name==='__clipboardKey') {
    // This is a real Chromium shortcut, not a synthetic paste event. The binding
    // accepts only this test action, never arbitrary CDP methods.
    (async()=>{
      const copy=message.params.payload==='copy';
      if(!copy && message.params.payload!=='paste')throw new Error('Unknown clipboard action');
      const key=copy?'c':'v', code=copy?'KeyC':'KeyV', windowsVirtualKeyCode=copy?67:86;
      await call('Input.dispatchKeyEvent',{type:'keyDown',modifiers:2,key,code,windowsVirtualKeyCode});
      await call('Input.dispatchKeyEvent',{type:'keyUp',modifiers:0,key,code,windowsVirtualKeyCode});
    })().catch(error=>{console.error(String(error));process.exitCode=1;});
  }
};
try {
  if(process.env.PROBE_CLIPBOARD==='1') {
    await call('Runtime.enable');
    await call('Browser.grantPermissions',{origin:new URL(page.url).origin,permissions:['clipboardReadWrite','clipboardSanitizedWrite']});
    await call('Page.bringToFront');
    await call('Emulation.setFocusEmulationEnabled',{enabled:true});
    await call('Runtime.addBinding',{name:'__clipboardKey'});
    await call('Runtime.evaluate',{expression:`window.__checkClipboard=true;
      window.__trustedPaste=()=>new Promise((resolve,reject)=>{
        const canvas=document.querySelector('canvas');canvas.focus();
        const timeout=setTimeout(()=>reject(new Error('Browser did not deliver Ctrl+V paste')),5000);
        canvas.addEventListener('paste',()=>{clearTimeout(timeout);queueMicrotask(resolve);},{once:true});
        window.__clipboardKey('paste');
      });`});
  }
  const result=await call('Runtime.evaluate',{expression:await readFile(script,'utf8'),awaitPromise:true,returnByValue:true});
  console.log(JSON.stringify(result,null,2));
  if(result.exceptionDetails)process.exitCode=1;
} finally {
  for(const entry of pending.values())clearTimeout(entry.timer);
  socket.close();
}
