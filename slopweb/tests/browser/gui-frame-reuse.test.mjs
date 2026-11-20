import {test} from 'node:test';
import {installInputDom} from './input-dom-fixture.mjs';
import assert from 'node:assert/strict';
import {GuiFrameReuse,GuiPacketDecoder} from '../../runtime/wwwroot/gui-frame-reuse.js';
import {attachGuiInput} from '../../runtime/wwwroot/gui-input.js';
import {renderGui} from '../../runtime/wwwroot/gui.js';
const packet=[{kind:'matrix',value:[1,0,0,1,0,0]},{kind:'targetTexture',value:{id:1,rect:[0,0,10,10]}}];
test('packet decoding retains only the last bounded exact wire value and rejects malformed replacements',()=>{
  const decoder=new GuiPacketDecoder(),raw=JSON.stringify({commands:packet});
  const first=decoder.decode(raw);
  assert.equal(decoder.decode((' '+raw).slice(1)),first);
  assert.notEqual(decoder.decode(raw+' '),first);
  assert.notEqual(decoder.decode(raw),first);
  const large=JSON.stringify({commands:[],padding:'x'.repeat(2*1024*1024)});
  assert.notEqual(decoder.decode(large),decoder.decode(large));
  assert.equal(decoder.previous,null);
  decoder.decode(raw);assert.throws(()=>decoder.decode('{'),SyntaxError);
  assert.equal(decoder.previous,null);
});
test('wire identity never skips resource commands or changed packets',()=>{
  const cache=new GuiFrameReuse(),commands=[{kind:'imageBatch',value:{quads:'packed'}}];
  const raw=JSON.stringify({commands});
  const first=cache.prepare(commands,100,200,0,true,raw);cache.commit(first.key,100,200,1);
  assert.equal(cache.prepare(commands,100,200,1,true,raw).skip,true);
  assert.equal(cache.prepare(commands,100,200,1,true,raw+' ').skip,false);
  assert.equal(cache.prepare([{kind:'releaseImage',value:1}],100,200,1,true,raw).skip,false);
});
test('identical packets reuse only unchanged dimensions and resource revision',()=>{
  const cache=new GuiFrameReuse();
  const first=cache.prepare(packet,100,200,0,true);
  assert.equal(first.skip,false);
  cache.commit(first.key,100,200,1);
  assert.equal(cache.prepare(structuredClone(packet),100,200,1,true).skip,true);
  assert.equal(cache.prepare(packet,101,200,1,true).skip,false);
  assert.equal(cache.prepare(packet,100,200,2,true).skip,false);
  assert.equal(cache.prepare([...packet,{kind:'fill',value:{color:[1,0,0,1]}}],100,200,1,true).skip,false);
});
test('uploads, target writes, releases, animations and context loss invalidate reuse',()=>{
  const cache=new GuiFrameReuse();
  for(const kind of ['image','target','releaseImage','releaseTarget','unknown']) {
    const first=cache.prepare(packet,100,200,1,true);cache.commit(first.key,100,200,1);
    assert.equal(cache.prepare([{kind,value:1}],100,200,1,true).skip,false);
    assert.equal(cache.prepare(packet,100,200,1,true).skip,false);
  }
  let first=cache.prepare(packet,100,200,1,true);cache.commit(first.key,100,200,1);
  assert.equal(cache.prepare(packet,100,200,1,false).skip,false);
  assert.equal(cache.prepare(packet,100,200,1,true).skip,false);
  first=cache.prepare(packet,100,200,1,true);cache.commit(first.key,100,200,1);
  cache.invalidate();assert.equal(cache.prepare(packet,100,200,1,true).skip,false);
});

test('reuse skips Canvas work while native dispatch and clipboard pumping continue',()=>{
  let clears=0,draws=0,updates=0,clipboard=0;
  const context={resetTransform(){},clearRect(){clears++;},beginPath(){},roundRect(){},fill(){draws++;}};
  const canvas=Object.assign(installInputDom().canvas(),{getContext:()=>context});
  const commands=[{kind:'fill',value:{rect:[0,0,100,100],rounding:0,color:[1,0,0,1]}}];
  const samples=[];
  const input=attachGuiInput(canvas,{
    allowFrameReuse:true,
    GuiInputFrame(){updates++;return JSON.stringify({commands});},
    ClipboardTake(){clipboard++;return '[]';},
    profileFrame:sample=>samples.push(sample),
  });
  try {
    const first=input.frame(),second=input.frame();
    assert.equal(first.presentationUnchanged,false);assert.equal(second.presentationUnchanged,true);
    assert.equal(first.commands,second.commands);
    assert.equal(draws,1);assert.equal(clears,1);
    assert.equal(updates,2);assert.equal(clipboard,2);
    assert.equal(samples[1].canvasReused,1);
    renderGui(canvas,commands);input.frame();assert.equal(draws,3);
    canvas.dispatchEvent(new Event('contextrestored'));input.frame();assert.equal(draws,4);
    canvas.width=200;input.frame();assert.equal(draws,5);
  } finally {input.dispose();}
});

test('changing profile samples do not defeat command reuse and changed commands still repaint',()=>{
  let draws=0,frame=0;
  const context={resetTransform(){},clearRect(){},beginPath(){},roundRect(){},fill(){draws++;}};
  const canvas=Object.assign(installInputDom().canvas(),{getContext:()=>context});
  const input=attachGuiInput(canvas,{allowFrameReuse:true,GuiInputFrame:()=>JSON.stringify({
    commands:[{kind:'fill',value:{rect:[0,0,10,10],rounding:0,color:[frame>1?0:1,0,0,1]}}],profile:{frame:frame++}
  })});
  try {
    const first=input.frame(),second=input.frame(),third=input.frame();
    assert.equal(second.presentationUnchanged,true);assert.equal(third.presentationUnchanged,false);
    assert.equal(first.profile.frame,0);assert.equal(second.profile.frame,1);assert.equal(third.profile.frame,2);
    assert.equal(draws,2);
  } finally {input.dispose();}
});

test('only the retained full packet can be acknowledged and each repeat keeps fresh profiling',()=>{
  const decoder=new GuiPacketDecoder();
  const raw=JSON.stringify({packet:7,commands:packet,profile:{frame:1}});
  assert.equal(decoder.packetId,0);
  const commands=decoder.decode(raw).commands;
  assert.equal(decoder.packetId,7);
  const repeat=decoder.decode('{"repeat":7,"profile":{"frame":2}}');
  assert.equal(repeat.commands,commands);
  assert.equal(repeat.profile.frame,2);
  assert.equal(decoder.decode('{"repeat":7}').profile,undefined);
  assert.throws(()=>decoder.decode('{"repeat":8}'),/retained packet/);
  assert.equal(decoder.packetId,0);
  assert.throws(()=>decoder.decode('{"repeat":7}'),/retained packet/);
  decoder.decode(JSON.stringify({packet:8,commands:[],padding:'x'.repeat(2*1024*1024)}));
  assert.equal(decoder.packetId,0);
});

test('a compact repeat replays after resize or external drawing and when reuse is disabled',()=>{
  let draws=0,allowFrameReuse=true;
  const context={resetTransform(){},clearRect(){},beginPath(){},roundRect(){},fill(){draws++;}};
  const canvas=Object.assign(installInputDom().canvas(),{getContext:()=>context});
  const commands=[{kind:'fill',value:{rect:[0,0,10,10],rounding:0,color:[1,0,0,1]}}];
  const acks=[];
  const input=attachGuiInput(canvas,{
    get allowFrameReuse(){return allowFrameReuse;},
    GuiInputFrame(batch,id){acks.push(id);return id===21?'{"repeat":21}':JSON.stringify({packet:21,commands});},
  });
  try {
    assert.equal(input.frame().presentationUnchanged,false);
    assert.equal(input.frame().presentationUnchanged,true);
    canvas.width++;input.frame();assert.equal(draws,2);
    renderGui(canvas,[]);input.frame();assert.equal(draws,3);
    allowFrameReuse=false;input.frame();assert.equal(draws,4);
    assert.deepEqual(acks,[0,21,21,21,21]);
  } finally {input.dispose();}
});
