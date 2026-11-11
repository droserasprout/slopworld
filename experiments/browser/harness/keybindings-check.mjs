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
// Seed only an authored binding, then prove native shortcut dispatch uses both
// restored slots. Installed-API tests separately cover the native Save hook.
const key='slopworld.keybindings.v1';
const waitFor=async condition=>{
  const deadline=Date.now()+180000;
  while(!await condition()) {
    if(Date.now()>deadline)throw new Error('Key binding reload timed out');
    await new Promise(resolve=>setTimeout(resolve,250));
  }
};
const reload=async()=>{
  const origin=await evaluate('performance.timeOrigin');
  await call('Page.reload',{ignoreCache:true});
  await waitFor(()=>evaluate(`performance.timeOrigin!==${origin} && window.__workspace?.status==='running'`));
};
const setting=()=>evaluate(`(() => {window.__managedProbe.WorkspaceSavePreferences();return JSON.parse(localStorage.getItem('slopworld.presentation.v1')).preferences.sidebarOpenFilesVisible;})()`);
let stored,original;
try {
  await waitFor(()=>evaluate("window.__workspace?.status==='running'"));
  stored=await evaluate(`localStorage.getItem('${key}')`);
  original=await setting();
  await evaluate(`localStorage.setItem('${key}',JSON.stringify({version:1,bindings:{SlopToggleOpenFiles:{a:'F13',b:'F14'}}}))`);
  await reload();
  for(const [binding,expected] of [['F13',!original],['F14',original]]) {
    await evaluate(`(() => {const canvas=document.querySelector('canvas:not(#loading-screen)');for(const type of ['keydown','keyup'])canvas.dispatchEvent(new KeyboardEvent(type,{key:'${binding}',code:'${binding}',bubbles:true}));})()`);
    await waitFor(async()=>await setting()===expected);
  }
  console.log(JSON.stringify({reload:true,nativePrimary:true,nativeAlternate:true,layoutRestored:true}));
} finally {
  try {
  if(stored!==undefined) {
    await evaluate(stored===null?`localStorage.removeItem('${key}')`:`localStorage.setItem('${key}',${JSON.stringify(stored)})`);
    if(original!==undefined)await evaluate(`window.__managedProbe.WorkspaceAppearance(${JSON.stringify(JSON.stringify({sidebarOpenFilesVisible:original}))});window.__managedProbe.WorkspaceSavePreferences();`);
    await reload();
  }
  } finally {
    for(const entry of pending.values())clearTimeout(entry.timer);
    socket.close();
  }
}
