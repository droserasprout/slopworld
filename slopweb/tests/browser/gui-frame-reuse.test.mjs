import {test} from 'node:test';
import {installInputDom} from './input-dom-fixture.mjs';
import assert from 'node:assert/strict';
import {GuiFrameReuse} from '../../runtime/wwwroot/gui-frame-reuse.js';
import {attachGuiInput} from '../../runtime/wwwroot/gui-input.js';
import {renderGui} from '../../runtime/wwwroot/gui.js';
const packet=[{kind:'matrix',value:[1,0,0,1,0,0]},{kind:'targetTexture',value:{id:1,rect:[0,0,10,10]}}];
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
    input.frame();input.frame();
    assert.equal(draws,1);assert.equal(clears,1);
    assert.equal(updates,2);assert.equal(clipboard,2);
    assert.equal(samples[1].canvasReused,1);
    renderGui(canvas,commands);input.frame();assert.equal(draws,3);
    canvas.dispatchEvent(new Event('contextrestored'));input.frame();assert.equal(draws,4);
    canvas.width=200;input.frame();assert.equal(draws,5);
  } finally {input.dispose();}
});
