// Resume a retained headless diagnostic page without reloading its runtime.
import {readFile} from 'node:fs/promises';
const [profile]=process.argv.slice(2);
const port=(await readFile(`${profile}/DevToolsActivePort`,'utf8')).split('\n')[0];
const targets=await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
const page=targets.find(target=>target.type==='page' && target.url.startsWith('http://127.0.0.1:'));
if(!page) throw new Error('No local retained page');
const socket=new WebSocket(page.webSocketDebuggerUrl);
await new Promise((resolve,reject)=>{socket.onopen=resolve;socket.onerror=reject;});
let serial=0;
const pending=new Map();
socket.onmessage=({data})=>{
  const message=JSON.parse(data),entry=pending.get(message.id);
  if(!entry) return;
  pending.delete(message.id);clearTimeout(entry.timer);
  if(message.error) entry.reject(new Error(JSON.stringify(message.error)));else entry.resolve(message.result);
};
const call=(method,params={})=>new Promise((resolve,reject)=>{
  const id=++serial,timer=setTimeout(()=>reject(new Error(`Timeout: ${method}`)),10000);
  pending.set(id,{resolve,reject,timer});socket.send(JSON.stringify({id,method,params}));
});
try {
  await call('Page.setWebLifecycleState',{state:'active'});
  await call('Page.bringToFront');
  console.log(JSON.stringify(await call('Runtime.evaluate',{
    expression:'Promise.race([new Promise(resolve=>requestAnimationFrame(()=>resolve({animationFrame:true,frame:window.__workspace?.frames}))),new Promise(resolve=>setTimeout(()=>resolve({animationFrame:false,frame:window.__workspace?.frames}),1000))])',
    awaitPromise:true,returnByValue:true,
  })));
} finally {for(const entry of pending.values()) clearTimeout(entry.timer);socket.close();}
