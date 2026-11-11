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
  const result=await call('Runtime.evaluate',{expression,returnByValue:true});
  if(result.exceptionDetails)throw new Error(JSON.stringify(result.exceptionDetails));return result.result.value;
};
try {
  await call('Page.navigate',{url:new URL('/',page.url).href});
  const deadline=Date.now()+180000;
  while(!await evaluate("window.__workspace?.status==='running'")) {
    if(Date.now()>deadline)throw new Error('Root workspace did not start');
    await new Promise(resolve=>setTimeout(resolve,1000));
  }
  const results=[];
  for(const [width,height,deviceScaleFactor] of [[1000,700,1],[1200,800,2]]) {
    await call('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor,mobile:false});
    const limit=Date.now()+10000;
    let state;
    do {
      await new Promise(resolve=>setTimeout(resolve,150));
      state=await evaluate(`({url:location.href,canvas:[document.querySelector('canvas').width,document.querySelector('canvas').height],state:JSON.parse(window.__managedProbe.WorkspaceState())})`);
      if(Date.now()>limit)throw new Error('Resize failed: '+JSON.stringify(state));
    } while(state.state.screenWidth!==width*deviceScaleFactor || state.state.screenHeight!==height*deviceScaleFactor || state.state.uiScale!==deviceScaleFactor);
    if(state.url!==new URL('/',page.url).href)throw new Error('Root URL was rewritten: '+state.url);
    if(state.state.game.hasGame||state.state.game.hasWorld||state.state.game.ticks)throw new Error('Unexpected simulation');
    results.push({size:state.canvas,scale:state.state.uiScale,session:state.state.session,url:state.url});
  }
  console.log(JSON.stringify({responsive:true,results}));
} finally {
  await call('Emulation.clearDeviceMetricsOverride').catch(()=>{});
  for(const entry of pending.values())clearTimeout(entry.timer);
  socket.close();
}
