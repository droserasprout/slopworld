import test from 'node:test';
import assert from 'node:assert/strict';
import {renderGuiProbe} from '../../runtime/wwwroot/gui.js';

const canvas=alpha=>({width:1,height:1,getContext:()=>({
  getImageData:()=>({data:new Uint8ClampedArray([209,224,237,alpha])}),
})});

test('workspace startup accepts native glyph batches without diagnostic text commands',()=>{
  const result=renderGuiProbe([{kind:'imageBatch',value:{}}],false,canvas(255));
  assert.equal(result.visiblePixels,1);
});
test('workspace startup accepts a retained terminal image',()=>{
  assert.equal(renderGuiProbe([{kind:'targetTexture',value:{}}],false,canvas(255)).visiblePixels,1);
});
test('workspace startup still rejects an empty framebuffer',()=>{
  assert.throws(()=>renderGuiProbe([{kind:'imageBatch',value:{}}],false,canvas(0)),/no visible pixels/);
});
