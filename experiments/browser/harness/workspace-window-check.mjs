// Trusted input against an isolated prepared workspace; restore its bindings on exit.
import {readFile} from 'node:fs/promises';
import assert from 'node:assert/strict';
const [profile]=process.argv.slice(2);
const port=(await readFile(`${profile}/DevToolsActivePort`,'utf8')).split('\n')[0];
const targets=await(await fetch(`http://127.0.0.1:${port}/json/list`)).json();
const page=targets.find(t=>t.type==='page'&&t.url.startsWith('http://127.0.0.1:'));
const socket=new WebSocket(page.webSocketDebuggerUrl);
await new Promise((resolve,reject)=>{socket.onopen=resolve;socket.onerror=reject;});
let serial=0;const pending=new Map();
const call=(method,params={})=>new Promise((resolve,reject)=>{
  const id=++serial,timer=setTimeout(()=>{pending.delete(id);reject(Error(method+' timed out'));},30000);
  pending.set(id,{resolve,reject,timer});socket.send(JSON.stringify({id,method,params}));
});
socket.onmessage=({data})=>{const message=JSON.parse(data),entry=pending.get(message.id);if(entry){pending.delete(message.id);clearTimeout(entry.timer);message.error?entry.reject(Error(JSON.stringify(message.error))):entry.resolve(message.result);}};
const evaluate=async expression=>{
  const result=await call('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});
  if(result.exceptionDetails)throw Error(JSON.stringify(result.exceptionDetails));return result.result.value;
};
const delay=()=>new Promise(resolve=>setTimeout(resolve,300));
const key=async(key,code,windowsVirtualKeyCode)=>{
  await call('Input.dispatchKeyEvent',{type:'keyDown',key,code,windowsVirtualKeyCode});
  await call('Input.dispatchKeyEvent',{type:'keyUp',key,code,windowsVirtualKeyCode});
  await delay();
};
const fullscreen=async expected=>{
  for(let i=0;i<40;i++){
    if(await evaluate('document.fullscreenElement===document.documentElement')===expected)return;
    await delay();
  }
  throw Error('Native fullscreen binding did not settle to '+expected);
};
const click=async point=>{
  await call('Input.dispatchMouseEvent',{type:'mouseMoved',...point});
  await call('Input.dispatchMouseEvent',{type:'mousePressed',button:'left',clickCount:1,...point});
  await call('Input.dispatchMouseEvent',{type:'mouseReleased',button:'left',clickCount:1,...point});
  await delay();
};
const clickLabel=async text=>{
  const point=await evaluate(`(()=>{const label=JSON.parse(window.__managedProbe.WorkspaceLabels()).find(l=>l.text===${JSON.stringify(text)});if(!label)throw Error('Missing native label '+${JSON.stringify(text)});const canvas=document.querySelector('canvas:not(#loading-screen)'),r=canvas.getBoundingClientRect();return {x:r.left+(label.x+5)*r.width/canvas.width,y:r.top+(label.y+label.height/2)*r.height/canvas.height};})()`);
  await click(point);
};
let stored;
try {
  assert.equal(await evaluate('window.__workspace?.status'),'running');
  stored=await evaluate("localStorage.getItem('slopworld.keybindings.v1')");
  await evaluate(`window.__managedProbe.WorkspaceKeyBindings(JSON.stringify({SlopToggleFullscreen:{a:'F11',b:'None'}}),()=>{})`);
  // Focus the existing editable proxy without dispatching a click into a terminal.
  await evaluate("document.querySelector('textarea')?.focus()");
  await key('F11','F11',122);await fullscreen(true);
  await key('F11','F11',122);await fullscreen(false);
  await evaluate("window.__managedProbe.WorkspaceKeyBindings(JSON.stringify({SlopToggleFullscreen:{a:'G',b:'None'}}),()=>{})");
  await key('g','KeyG',71);await fullscreen(true);
  await key('g','KeyG',71);await fullscreen(false);
  await key('g','KeyG',71);await fullscreen(true);
  await key('Escape','Escape',27);
  const escapeExited=await evaluate('!document.fullscreenElement');
  assert.equal(escapeExited,true,'Escape did not leave workspace fullscreen');
  await fullscreen(false);
  await evaluate('window.__managedProbe.WorkspaceObserveLabels(true)');
  await click(await evaluate(`(()=>{const state=JSON.parse(window.__managedProbe.WorkspaceState()),[x,y,w,h]=state.doors.config.rect,r=document.querySelector('canvas:not(#loading-screen)').getBoundingClientRect();return {x:r.left+(x+w/2)*r.width/state.uiWidth,y:r.top+(y+h/2)*r.height/state.uiHeight};})()`));
  await clickLabel('Display');
  await clickLabel('Fullscreen');await fullscreen(true);
  await clickLabel('Fullscreen');await fullscreen(false);
  const state=await evaluate('JSON.parse(window.__managedProbe.WorkspaceState())');
  assert.equal(state.game.hasGame,false);assert.equal(state.game.hasWorld,false);
  console.log(JSON.stringify({nativeDefault:true,nativePrintableRebind:true,settingsToggle:true,browserExit:true,escapeExited,game:state.game}));
} finally {
  try {
    await evaluate('document.fullscreenElement ? document.exitFullscreen() : null');
    if(stored!==undefined){
      await evaluate(`localStorage.${stored===null?"removeItem('slopworld.keybindings.v1')":`setItem('slopworld.keybindings.v1',${JSON.stringify(stored)})`}`);
      // Reload restores defaults as well as explicit slots; Apply({}) is an overlay.
      const origin=await evaluate('performance.timeOrigin');
      await call('Page.reload',{ignoreCache:true});
      const deadline=Date.now()+180000;
      while(!await evaluate(`performance.timeOrigin!==${origin} && window.__workspace?.status==='running'`)){
        if(Date.now()>deadline)throw Error('Workspace did not reload after restoring bindings');
        await delay();
      }
    }
  } finally {
    for(const entry of pending.values())clearTimeout(entry.timer);
    socket.close();
  }
}
