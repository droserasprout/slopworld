// Actual Fullscreen API admission using trusted CDP keys; no game, assets or host page.
import {spawn} from 'node:child_process';
import {mkdtemp,readFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {setTimeout as delay} from 'node:timers/promises';
import {fileURLToPath} from 'node:url';
import assert from 'node:assert/strict';
import {serve} from './server.mjs';
const profile=await mkdtemp(`${tmpdir()}/slopweb-fullscreen-`);
const {server,url}=await serve(fileURLToPath(new URL('../runtime/wwwroot',import.meta.url)));
let chrome,socket;
const pending=new Map();
try {
  chrome=spawn(process.env.CHROMIUM??'chromium',['--headless=new','--remote-debugging-port=0',`--user-data-dir=${profile}`,'--no-first-run','--disable-dev-shm-usage','about:blank'],{stdio:'ignore'});
  let port;
  for(let i=0;i<100;i++) {
    try {port=(await readFile(`${profile}/DevToolsActivePort`,'utf8')).split('\n')[0];break;}
    catch {await delay(100);}
  }
  assert.ok(port,'Chromium did not start');
  const targets=await(await fetch(`http://127.0.0.1:${port}/json/list`)).json();
  socket=new WebSocket(targets.find(target=>target.type==='page').webSocketDebuggerUrl);
  await new Promise((resolve,reject)=>{socket.onopen=resolve;socket.onerror=reject;});
  let serial=0;
  const call=(method,params={})=>new Promise((resolve,reject)=>{
    const id=++serial,timer=setTimeout(()=>{pending.delete(id);reject(Error(`${method} timed out`));},10000);
    pending.set(id,{resolve,reject,timer});socket.send(JSON.stringify({id,method,params}));
  });
  socket.onmessage=({data})=>{const message=JSON.parse(data),entry=pending.get(message.id);if(entry){pending.delete(message.id);clearTimeout(entry.timer);message.error?entry.reject(Error(JSON.stringify(message.error))):entry.resolve(message.result);}};
  const evaluate=async expression=>{
    const response=await call('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});
    if(response.exceptionDetails)throw Error(JSON.stringify(response.exceptionDetails));
    return response.result.value;
  };
  await call('Page.navigate',{url:new URL('/workspace-fullscreen.js',url).href});
  // Wait for same-origin navigation before replacing the document with a tiny input fixture.
  for(let i=0;i<100&&!await evaluate(`location.origin===${JSON.stringify(new URL(url).origin)}`);i++)await delay(50);
  await evaluate(`(async()=>{
    document.body.innerHTML='<canvas tabindex="0"></canvas>';
    const {attachWorkspaceFullscreen}=await import('/workspace-fullscreen.js');
    window.states=[];
    window.bridge=attachWorkspaceFullscreen({WorkspaceWindow:callback=>window.requestWindow=callback,WorkspaceFullscreenState:value=>states.push(value)});
    document.addEventListener('keydown',event=>{if(event.code==='F11'){event.preventDefault();requestWindow(!document.fullscreenElement);}});
    document.querySelector('canvas').focus();
  })()`);
  const key=async()=>{await call('Input.dispatchKeyEvent',{type:'keyDown',key:'F11',code:'F11',windowsVirtualKeyCode:122});await call('Input.dispatchKeyEvent',{type:'keyUp',key:'F11',code:'F11',windowsVirtualKeyCode:122});};
  const waitState=async value=>{
    for(let i=0;i<100;i++){if(await evaluate(`states.at(-1)===${value} && (document.fullscreenElement===document.documentElement)===${value}`))return;await delay(50);}
    throw Error('Fullscreen state did not settle: '+await evaluate('JSON.stringify(states)'));
  };
  await key();await waitState(true);
  await key();await waitState(false);
  await key();await waitState(true);
  // Browser-owned exit uses the same fullscreenchange path as Escape/browser chrome.
  await evaluate('document.exitFullscreen()');await waitState(false);
  console.log(JSON.stringify({trustedKeyEnter:true,trustedKeyExit:true,browserExit:true,states:await evaluate('states')}));
} finally {
  for(const entry of pending.values())clearTimeout(entry.timer);
  socket?.close();chrome?.kill();server.closeAllConnections();await new Promise(resolve=>server.close(resolve));
  await rm(profile,{recursive:true,force:true,maxRetries:10,retryDelay:100});
}
