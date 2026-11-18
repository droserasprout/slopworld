import test from 'node:test';
import assert from 'node:assert/strict';
import {attachWorkspaceAudio} from '../../runtime/wwwroot/workspace-audio.js';

class Decoder extends EventTarget {
  paused = true; ended = false; volume = 1; src = ''; calls = 0; reject = false;
  play() { this.calls++; if(this.reject) return Promise.reject(new Error('blocked')); this.paused=false; return Promise.resolve(); }
  pause() { this.paused=true; }
  removeAttribute() {this.src='';}
  load() {}
}
const flush = () => new Promise(resolve => setImmediate(resolve));
function fixture() {
  const state={track:'pace-20261012',revision:0,play:true,volume:.4};
  const decoder=new Decoder(), target=new EventTarget(), reports=[];
  const managed={WorkspaceAudio:()=>JSON.stringify(state),WorkspaceAudioPlaying:value=>reports.push(value),
    WorkspaceAudioEnded(){state.track='dive-20261012';state.revision++;}};
  return {state,decoder,target,reports,bridge:attachWorkspaceAudio(managed,decoder,target)};
}
test('autoplay rejection waits for a gesture without frame retries',async()=>{
  const f=fixture(); f.decoder.reject=true;
  f.bridge.frame(); await flush();
  for(let i=0;i<120;i++)f.bridge.frame();
  assert.equal(f.decoder.calls,1); assert.equal(f.reports.at(-1),false);
  f.decoder.reject=false;f.target.dispatchEvent(new Event('pointerdown'));await flush();
  assert.equal(f.decoder.calls,2);assert.equal(f.reports.at(-1),true);
  f.bridge.dispose();
});
test('ended advances once, next restarts the selected track, and volume follows policy',async()=>{
  const f=fixture();f.bridge.frame();await flush();assert.equal(f.decoder.volume,.4);
  f.decoder.ended=true;f.decoder.paused=true;f.decoder.dispatchEvent(new Event('ended'));await flush();
  assert.match(f.decoder.src,/dive-20261012.ogg$/); assert.equal(f.decoder.calls,2);
  f.state.revision++;f.state.track='hime-20261012';f.state.volume=.15;f.bridge.frame();await flush();
  assert.match(f.decoder.src,/hime-20261012.ogg$/);assert.equal(f.decoder.calls,3);assert.equal(f.decoder.volume,.15);
  f.bridge.dispose();
});
test('mute and source handoff pause decoding; disposal removes gesture and ended handlers',async()=>{
  const f=fixture();f.bridge.frame();await flush();
  f.state.play=false;f.bridge.frame();assert.equal(f.decoder.paused,true);assert.equal(f.decoder.src,'');assert.equal(f.reports.at(-1),false);
  f.target.dispatchEvent(new Event('keydown'));await flush();assert.equal(f.decoder.calls,1);
  f.state.play=true;f.bridge.frame();await flush();assert.equal(f.decoder.calls,2);
  f.bridge.dispose();assert.equal(f.decoder.paused,true);assert.equal(f.decoder.src,'');
  f.decoder.dispatchEvent(new Event('ended'));f.target.dispatchEvent(new Event('pointerdown'));await flush();
  assert.equal(f.decoder.calls,2);assert.equal(f.state.track,'pace-20261012');
});
test('late play completion cannot restart audio after a source switch or disposal',async()=>{
  const f=fixture();let complete;
  f.decoder.play=function(){this.calls++;return new Promise(resolve=>{complete=()=>{this.paused=false;resolve();};});};
  f.bridge.frame();f.state.play=false;f.bridge.frame();complete();await flush();assert.equal(f.decoder.paused,true);
  f.state.play=true;f.bridge.frame();f.bridge.dispose();complete();await flush();assert.equal(f.decoder.paused,true);
});
test('decoder errors retire playback until a keyboard gesture requests recovery',async()=>{
  const f=fixture();f.bridge.frame();await flush();
  f.decoder.dispatchEvent(new Event('error'));assert.equal(f.decoder.paused,true);assert.equal(f.reports.at(-1),false);
  for(let i=0;i<10;i++)f.bridge.frame();assert.equal(f.decoder.calls,1);
  f.target.dispatchEvent(new Event('keydown'));await flush();assert.equal(f.decoder.calls,2);
  f.bridge.dispose();
});
