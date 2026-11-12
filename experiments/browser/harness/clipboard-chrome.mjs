// Exercise trusted Chromium Ctrl+V against production DOM adapters and game-free
// callbacks. This deliberately never loads a workspace or contacts a daemon.
import {spawn} from 'node:child_process';
import {mkdtemp,readFile,rm} from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {setTimeout as delay} from 'node:timers/promises';
import {serve} from '../../../slopweb/server/server.mjs';

const profile=await mkdtemp(path.join(os.tmpdir(),'slopworld-clipboard-'));
const root=fileURLToPath(new URL('../../../slopweb/runtime/wwwroot/',import.meta.url));
const {server,url}=await serve(root);
let chrome,socket;
const pending=new Map();
try {
  chrome=spawn(process.env.CHROMIUM ?? 'chromium',[
    '--headless=new','--remote-debugging-port=0',`--user-data-dir=${profile}`,
    '--no-first-run','--no-default-browser-check','--disable-dev-shm-usage','about:blank',
  ],{stdio:['ignore','ignore','pipe']});
  let stderr=''; chrome.stderr.on('data',data=>{stderr=(stderr+data).slice(-4000)});
  chrome.on('error',error=>{stderr+=String(error)});
  let port;
  for(let i=0;i<100;i++) {
    try { port=(await readFile(path.join(profile,'DevToolsActivePort'),'utf8')).split('\n')[0]; break; }
    catch { await delay(100); }
  }
  if(!port) throw Error(`Chrome failed to start: ${stderr}`);
  const targets=await(await fetch(`http://127.0.0.1:${port}/json/list`)).json();
  socket=new WebSocket(targets.find(x=>x.type === 'page').webSocketDebuggerUrl);
  await new Promise((resolve,reject)=>{socket.onopen=resolve;socket.onerror=reject});
  let serial=0;
  socket.onmessage=({data})=>{
    const message=JSON.parse(data), entry=pending.get(message.id);
    if(!entry)return;
    pending.delete(message.id); clearTimeout(entry.timer);
    if(message.error)entry.reject(Error(JSON.stringify(message.error)));else entry.resolve(message.result);
  };
  const call=(method,params={})=>new Promise((resolve,reject)=>{
    const id=++serial;
    const timer=setTimeout(()=>{pending.delete(id);reject(Error(`Timed out: ${method}`))},15000);
    pending.set(id,{resolve,reject,timer});socket.send(JSON.stringify({id,method,params}));
  });
  const evaluate=async expression=>{
    const result=await call('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true});
    if(result.exceptionDetails)throw Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
  };
  await call('Page.enable');
  // A served JS document is inert HTML here and cannot start the game runtime.
  await call('Page.navigate',{url:url+'/clipboard.js'});
  for(let i=0;i<100;i++) {
    if(await evaluate('location.pathname === "/clipboard.js" && document.readyState === "complete"'))break;
    await delay(50);
  }
  await call('Browser.grantPermissions',{origin:new URL(url).origin,permissions:['clipboardReadWrite','clipboardSanitizedWrite']});
  await call('Emulation.setFocusEmulationEnabled',{enabled:true});
  await evaluate(`(async()=>{
    const {attachGuiInput}=await import('/gui-input.js');
    const canvas=document.createElement('canvas');canvas.tabIndex=0;document.body.replaceChildren(canvas);
    const state=window.clipboardCheck={batches:[],images:[],trusted:false,selectedPane:1,pending:[]};
    // Defer byte conversion until after the test switches its selected pane.
    let release;state.readGate=new Promise(resolve=>release=resolve);state.release=release;
    const original=Blob.prototype.arrayBuffer;
    Blob.prototype.arrayBuffer=async function(){await state.readGate;return original.call(this)};
    canvas.addEventListener('paste',event=>{state.trusted=event.isTrusted});
    state.input=attachGuiInput(canvas,{
      GuiInputFrame:raw=>{
        const batch=JSON.parse(raw);state.batches.push(batch);
        if(batch.some(x=>x.kind === 'key' && x.key === 'V')) {
          const data=batch.find(x=>x.kind === 'clipboard');
          if(!data?.image || data.text)throw Error('Expected image-only paste');
          state.pending.push({id:state.selectedPane,kind:'eventImage',images:true});
        }
        return '{"commands":[]}';
      },
      ClipboardTake:()=>JSON.stringify(state.pending.splice(0)),
      ClipboardComplete:(id,text,error)=>{throw Error('Unexpected text/error: '+error)},
      ClipboardCompleteImage:(id,base64,error)=>state.images.push({id,base64,error}),
    });
    const png=document.createElement('canvas');png.width=png.height=1;
    const blob=await new Promise(resolve=>png.toBlob(resolve,'image/png'));
    await navigator.clipboard.write([new ClipboardItem({'image/png':blob})]);
    canvas.focus();
  })()`);
  await call('Input.dispatchKeyEvent',{type:'keyDown',modifiers:2,key:'v',code:'KeyV',windowsVirtualKeyCode:86});
  await call('Input.dispatchKeyEvent',{type:'keyUp',modifiers:0,key:'v',code:'KeyV',windowsVirtualKeyCode:86});
  for(let i=0;i<100;i++) {
    if(await evaluate('clipboardCheck.trusted'))break;
    await delay(50);
  }
  await evaluate('clipboardCheck.selectedPane=2;clipboardCheck.release()');
  for(let i=0;i<100;i++) {
    if(await evaluate('clipboardCheck.images.length'))break;
    await delay(50);
  }
  const result=await evaluate(`(()=>{
    const s=clipboardCheck, image=s.images[0];
    if(!s.trusted || s.images.length !== 1 || image.id !== 1 || s.batches.length !== 1)
      throw Error('Trusted paste or original callback routing failed: '+JSON.stringify(s));
    const bytes=atob(image.base64);
    if(!bytes.startsWith(String.fromCharCode(137,80,78,71,13,10,26,10)))throw Error('Missing PNG bytes');
    return {trustedCtrlV:true,pngBytes:bytes.length,originalRequestId:image.id,selectedPane:s.selectedPane,nativeKeyBatches:s.batches.length};
  })()`);
  console.log(JSON.stringify(result));
} finally {
  for(const entry of pending.values())clearTimeout(entry.timer);
  socket?.close(); chrome?.kill();
  await new Promise(resolve=>server.close(resolve));
  await rm(profile,{recursive:true,force:true,maxRetries:10,retryDelay:100});
}
