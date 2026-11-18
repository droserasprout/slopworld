import test from 'node:test';
import assert from 'node:assert/strict';
import { tintPixels } from '../../runtime/wwwroot/gui-tint.js';

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
  const gui=await import('../../runtime/wwwroot/gui.js');
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
    // Canvas pixels are mocked; declared dimensions exercise the production
    // retention policy without allocating dozens of large native surfaces.
    for(const [size,capacity] of [[512,32],[1024,8],[4096,1]]) {
      const palette=Array.from({length:capacity+1},(_,i)=>({kind:'imageTexture',value:{
        id,color:[i/64,.8,.6,1],uv:[0,0,1,1],rect:[0,0,1,1]
      }}));
      gui.configureGuiTintProfile(true);
      gui.renderGui(screen,[{...upload,value:{...upload.value,width:size,height:size}},...palette.slice(0,capacity)]);
      assert.equal(gui.guiTintProfile().imageMisses,capacity);
      // Refresh the oldest entry, overflow, then check actual LRU eviction.
      gui.renderGui(screen,[palette[0],palette[capacity]]);
      assert.equal(gui.guiTintProfile().imageMisses,capacity+1);
      if(capacity>1) {
        gui.renderGui(screen,[palette[0]]);
        assert.equal(gui.guiTintProfile().imageMisses,capacity+1);
        gui.renderGui(screen,[palette[1]]);
      } else gui.renderGui(screen,[palette[0]]);
      assert.equal(gui.guiTintProfile().imageMisses,capacity+2);
    }
    gui.renderGui(screen,[{kind:'releaseImage',value:id}]);
    assert.throws(()=>gui.renderGui(screen,commands),/Missing GUI image/);
  } finally {
    gui.renderGui(screen,[{kind:'releaseImage',value:id}]);
    gui.configureGuiTintCache(previousCache);gui.configureGuiTintProfile(previousProfile);
    globalThis.document=previousDocument;globalThis.ImageData=previousImageData;
  }
});

test('restored upload, tint and uniform canvases redraw from the original image',async()=>{
  const gui=await import('../../runtime/wwwroot/gui.js');
  const previousDocument=globalThis.document, previousImage=globalThis.ImageData;
  const surfaces=[];
  let sampled;
  class Surface extends EventTarget {
    constructor(){
      super();
      this.context={
        putImageData:image=>{this.pixels=image.data.slice();},
        getImageData:()=>({data:this.pixels.slice()}),
        drawImage:source=>{sampled=source;},
        resetTransform(){},save(){},restore(){},
      };
    }
    getContext(){return this.context;}
    restore(){
      this.pixels.fill(0);
      this.dispatchEvent(new Event('contextrestored'));
    }
  }
  globalThis.document={createElement(){const surface=new Surface();surfaces.push(surface);return surface;}};
  globalThis.ImageData=class {constructor(data){this.data=data;}};
  const screen=new Surface(), id=999;
  const image=color=>({kind:'imageTexture',value:{id,color,uv:[0,0,1,1],rect:[0,0,2,1]}});
  const draw=color=>{gui.renderGui(screen,[image(color)]);return sampled.pixels;};
  try {
    for(const uniform of [false,true]) {
      surfaces.length=0;
      const pixels=[200,100,50,255,uniform?200:42,100,50,255];
      gui.renderGui(screen,[{kind:'image',value:{id,width:2,height:1,pixels:Buffer.from(pixels).toString('base64')}}]);
      const white=[1,1,1,1],brown=[.5,.75,1,1];
      // Restore the uploaded canvas before any tint readback has occurred.
      surfaces[0].restore();
      assert.deepEqual([...draw(white)],uniform?pixels.slice(0,4):pixels);
      for(const color of [white,brown]) {
        const expected=draw(color).slice(), revision=gui.guiRenderRevision();
        sampled.restore();
        assert.ok(gui.guiRenderRevision()>revision);
        assert.deepEqual(draw(color),expected);
      }
    }
  } finally {
    gui.renderGui(screen,[{kind:'releaseImage',value:id}]);
    globalThis.document=previousDocument;globalThis.ImageData=previousImage;
  }
});
