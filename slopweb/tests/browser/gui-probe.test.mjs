import test from 'node:test';
import assert from 'node:assert/strict';
import {inspectWorkspaceCanvas} from '../../runtime/wwwroot/workspace-diagnostics.js';

const canvas=alpha=>({width:1,height:1,getContext:()=>({
  getImageData:()=>({data:new Uint8ClampedArray([209,224,237,alpha])}),
})});

test('workspace diagnostics count visible pixels in the presented canvas',()=>{
  const result=inspectWorkspaceCanvas(canvas(255));
  assert.equal(result.visiblePixels,1);
});
test('workspace startup still rejects an empty framebuffer',()=>{
  assert.throws(()=>inspectWorkspaceCanvas(canvas(0)),/no visible pixels/);
});
