// Execute both single-key phases without starting a browser or contacting a daemon.
import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import vm from 'node:vm';

const source=(await readFile(new URL('./benchmark-workload.js',import.meta.url),'utf8'))
  .replace("await import('./gui.js')",'window.renderer');
const message=(tag,value)=>{
  const bytes=typeof value==='string'?new TextEncoder().encode(value):value;
  return new Uint8Array([tag,bytes.length,...bytes]);
};
async function fixture({failSetup=false}={}) {
  let clock=0,profile=false,command='',raw=null,index=0,finished=0;
  const keys=[],markers=new Set(),phases=[];
  class Socket {
    listeners=new Set();
    addEventListener(name,listener){this.listeners.add(listener);}
    removeEventListener(name,listener){this.listeners.delete(listener);}
    send(){}
  }
  const originalSend=Socket.prototype.send;
  const socket=new Socket();
  const game={hasGame:false,hasWorld:false,maps:null,ticks:0};
  const window={renderer:{renderGui(){}},__workspaceProfile:[],__workspace:{status:'running',frames:1,
    setProfiling(value){profile=value;phases.push(value);window.__workspaceProfile=value?[]:null;}},
    __managedProbe:{WorkspaceState:()=>JSON.stringify({session:'owned',game}),
      WorkspaceTerminalContains:(name,marker)=>markers.has(marker)}};
  const canvas={focus(){},dispatchEvent(event){
    if(event.type!=='keydown')return;
    if(!raw){
      if(event.key!=='Enter'){command+=event.key;return;}
      if(failSetup)throw Error('fixture setup failed');
      assert.equal(profile,false,'setup is outside profiled frames');
      const [kind,id]=command.split(' ');command='';
      raw={inline:kind==='inlinekeys',id};index=0;
      markers.add((raw.inline?'INLINE_':'')+'KEYS_READY_'+id);return;
    }
    if(event.key==='Enter'){
      assert.equal(index,20);assert.equal(profile,false,'finish is outside profiled frames');
      markers.add('INLINE_KEYS_DONE_'+raw.id);finished++;raw=null;return;
    }
    keys.push({key:event.key,code:event.code,profile});
    const prefix=raw.inline?'INLINE_':'';
    const marker=prefix+'KEY_'+raw.id+'_'+String(index).padStart(2,'0')+'_'+event.key.charCodeAt(0).toString(16);
    socket.send(message(34,message(10,'owned')));
    const screen=new Uint8Array([...message(10,'owned'),...message(18,marker)]);
    for(const listener of socket.listeners)listener({data:message(42,screen).buffer});
    markers.add(marker);window.__workspaceProfile?.push({targetWrites:1});index++;
    if(index===20 && !raw.inline){markers.add('KEYS_DONE_'+raw.id);raw=null;}
  }};
  await vm.runInNewContext(source,{window,document:{querySelector:()=>canvas},WebSocket:Socket,
    KeyboardEvent:class {constructor(type,args){Object.assign(this,{type},args);}},
    TextEncoder,TextDecoder,Uint8Array,ArrayBuffer,
    performance:{now:()=>++clock},requestAnimationFrame(){},setTimeout(resolve){resolve();}});
  window.__benchmark.created.push('owned');
  return {window,keys,phases,socket,originalSend,Socket,finished:()=>finished};
}
for(const phase of ['keystrokes','inlinekeystrokes'])
test(phase+' verifies twenty byte echoes with wire timing and restores observers',async()=>{
  const f=await fixture();
  for(let run=0;run<2;run++){
    const result=await f.window.__benchmark.run(phase,{stageProfile:run===0});
    assert.equal(result.latencyMs.length,20);assert.equal(result.wireTimings.length,20);
    for(const timing of result.wireTimings)
      for(const value of Object.values(timing))assert.equal(typeof value,'number');
    assert.equal(result.stages.length,run===0?20:0);
    assert.equal(f.Socket.prototype.send,f.originalSend);assert.equal(f.socket.listeners.size,0);
  }
  assert.equal(f.keys.length,40);
  assert.ok(f.keys.every(({key,code,profile},i)=>code==='Key'+key.toUpperCase() && profile===(i<20)));
  assert.equal(f.finished(),phase==='inlinekeystrokes'?2:0);
  assert.equal(f.phases.at(-1),false);
});
test('single-key setup failure restores the WebSocket observer and profiling',async()=>{
  const f=await fixture({failSetup:true});
  await assert.rejects(f.window.__benchmark.run('inlinekeystrokes'),/fixture setup failed/);
  assert.equal(f.Socket.prototype.send,f.originalSend);assert.equal(f.socket.listeners.size,0);
  assert.equal(f.phases.at(-1),false);
});
