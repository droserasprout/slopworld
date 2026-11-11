import test from 'node:test';
import assert from 'node:assert/strict';
import { tintPixels } from '../runtime/wwwroot/gui-tint.js';

test('tint pixels retain the original byte rounding, alpha and source', () => {
  const pixels = Uint8ClampedArray.from({length: 4096}, (_, i) => (i * 73 + (i >> 2)) % 256);
  const original = pixels.slice();
  for (const color of [[0, 0, 0], [1, 1, 1], [.5, .1, .9], [1/255, 128/255, 254/255]]) {
    const expected = pixels.slice();
    for (let i = 0; i < expected.length; i++) if (i % 4 !== 3) expected[i] *= color[i % 4];
    assert.deepEqual(tintPixels(pixels, color), expected);
    assert.deepEqual(pixels, original);
  }
});

test('expanded tint retention preserves draws and invalidates on replacement and release', async () => {
  const gui=await import('../runtime/wwwroot/gui.js');
  const previousDocument=globalThis.document,previousImageData=globalThis.ImageData;
  const drawn=[];
  const canvas=()=>{
    let pixels=new Uint8ClampedArray([200,100,50,255]);
    const ctx={resetTransform(){},save(){},restore(){},
      getImageData:()=>({data:pixels}),putImageData:image=>{pixels=image.data;},
      drawImage:source=>drawn.push([...source.getContext().getImageData().data])};
    return {width:1,height:1,getContext:()=>ctx};
  };
  globalThis.document={createElement:canvas};
  globalThis.ImageData=class {constructor(data){this.data=data;}};
  const screen=canvas(),id=998;
  const upload={kind:'image',value:{id,width:1,height:1,pixels:btoa(String.fromCharCode(200,100,50,255))}};
  const commands=Array.from({length:20},(_,i)=>({kind:'imageTexture',value:{id,color:[i/32,.8,.6,1],uv:[0,0,1,1],rect:[0,0,1,1]}}));
  const previousCache=gui.configureGuiTintCache(false),previousProfile=gui.configureGuiTintProfile(true);
  try {
    gui.renderGui(screen,[upload,...commands,...commands]);
    const reference=drawn.splice(0);
    assert.equal(gui.guiTintProfile().imageMisses,40);
    gui.configureGuiTintCache(true);gui.configureGuiTintProfile(true);
    gui.renderGui(screen,[...commands,...commands]);
    assert.deepEqual(drawn.splice(0),reference);
    assert.equal(gui.guiTintProfile().imageMisses,20);
    gui.renderGui(screen,[upload,...commands]);
    assert.equal(gui.guiTintProfile().imageMisses,40);
    gui.renderGui(screen,[{kind:'releaseImage',value:id}]);
    assert.throws(()=>gui.renderGui(screen,commands),/Missing GUI image/);
  } finally {
    gui.renderGui(screen,[{kind:'releaseImage',value:id}]);
    gui.configureGuiTintCache(previousCache);gui.configureGuiTintProfile(previousProfile);
    globalThis.document=previousDocument;globalThis.ImageData=previousImageData;
  }
});
