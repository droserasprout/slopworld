// Isolated retained diagnostic browser only; no terminal input or desktop capture.
import {readFile} from 'node:fs/promises';
const [profile]=process.argv.slice(2);
const port=(await readFile(`${profile}/DevToolsActivePort`,'utf8')).split('\n')[0];
const targets=await(await fetch(`http://127.0.0.1:${port}/json/list`)).json();
const page=targets.find(t=>t.type==='page'&&t.url.startsWith('http://127.0.0.1:'));
const socket=new WebSocket(page.webSocketDebuggerUrl);
await new Promise((resolve,reject)=>{socket.onopen=resolve;socket.onerror=reject;});
let serial=0;const pending=new Map();
const call=(method,params={})=>new Promise((resolve,reject)=>{
  const id=++serial,timer=setTimeout(()=>{pending.delete(id);reject(new Error(method+' timed out'));},30000);
  pending.set(id,{resolve,reject,timer});socket.send(JSON.stringify({id,method,params}));
});
socket.onmessage=({data})=>{const message=JSON.parse(data),entry=pending.get(message.id);if(entry){pending.delete(message.id);clearTimeout(entry.timer);message.error?entry.reject(new Error(JSON.stringify(message.error))):entry.resolve(message.result);}};
const evaluate=async expression=>{
  const result=await call('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});
  if(result.exceptionDetails)throw new Error(JSON.stringify(result.exceptionDetails));return result.result.value;
};
const storageKey='slopworld.presentation.v1';
const snapshot=()=>evaluate(`(() => { window.__managedProbe.WorkspaceSavePreferences(); return JSON.parse(localStorage.getItem('${storageKey}')).preferences; })()`);
const toggle=()=>evaluate(`(() => {
  const canvas=document.querySelector('canvas:not(#loading-screen)');
  for(const type of ['keydown','keyup']) canvas.dispatchEvent(new KeyboardEvent(type,{key:'F9',code:'F9',bubbles:true}));
})()`);
const waitFor=async condition=>{
  const until=Date.now()+180000;
  while(!await condition()) {
    if(Date.now()>until)throw new Error('Preference browser check timed out');
    await new Promise(resolve=>setTimeout(resolve,250));
  }
};
let original;
try {
  await waitFor(()=>evaluate("window.__workspace?.status==='running'"));
  original=await snapshot();
  const expected=!original.sidebarOpenFilesVisible;
  await toggle();
  // Do not force-save: prove the native dirty/debounce pump publishes the edit.
  await waitFor(()=>evaluate(`JSON.parse(localStorage.getItem('${storageKey}')).preferences.sidebarOpenFilesVisible === ${expected}`));
  const previousTimeOrigin=await evaluate('performance.timeOrigin');
  await call('Page.reload',{ignoreCache:true});
  await waitFor(()=>evaluate(`performance.timeOrigin!==${previousTimeOrigin} && window.__workspace?.status==='running'`));
  const reloaded=await snapshot();
  if(reloaded.sidebarOpenFilesVisible!==expected)throw new Error('Native setting did not survive reload');
  for(const forbidden of ['hostHome','token','endpoint','commandPaletteHistory','autoConnect'])
    if(Object.hasOwn(reloaded,forbidden))throw new Error('Non-presentation field was persisted: '+forbidden);
  await toggle();
  await waitFor(()=>evaluate(`JSON.parse(localStorage.getItem('${storageKey}')).preferences.sidebarOpenFilesVisible === ${original.sidebarOpenFilesVisible}`));
  const restored=await snapshot();
  if(restored.sidebarOpenFilesVisible!==original.sidebarOpenFilesVisible)throw new Error('Original layout was not restored');
  console.log(JSON.stringify({nativeShortcut:true,nativeDebouncedSave:true,reloadRestored:true,originalRestored:true}));
} finally {
  if(original) {
    // Restore only the browser-local presentation values even when a check fails.
    await evaluate(`window.__managedProbe.WorkspaceAppearance(${JSON.stringify(JSON.stringify({sidebarOpenFilesVisible:original.sidebarOpenFilesVisible}))}); window.__managedProbe.WorkspaceSavePreferences();`).catch(()=>{});
  }
  for(const entry of pending.values())clearTimeout(entry.timer);
  socket.close();
}
