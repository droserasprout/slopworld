// Host helper -> raw font files -> local FreeType, without a browser or game loop.
import assert from 'node:assert/strict';
import {readFileSync,mkdtempSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {resolve,join} from 'node:path';
import {fontProvider} from '../../../server/fonts.mjs';
import create from '../../../runtime/wwwroot/local-fonts.mjs';
import {localRasterizer} from '../../../runtime/wwwroot/workspace-rasterizer.js';
const game=process.env.RIMWORLD ?? `${process.env.HOME}/.local/share/Steam/steamapps/common/RimWorld`;
const cache=mkdtempSync(join(tmpdir(),'slopweb-font-delivery-'));
const raster=localRasterizer(await create({wasmBinary:readFileSync(new URL('../../../runtime/wwwroot/local-fonts.wasm',import.meta.url))}));
try {
  const provider=fontProvider({helper:resolve('slopweb/out/fonts.py'),game,cache});
  const selected=await provider({uiFontName:'PT Sans',fontName:'monospace'});
  assert.equal(selected.faces.length,4);
  for (const face of selected.faces) {
    const config=JSON.parse(face.bake);
    assert.equal(config.atlas,undefined,'startup must not bake glyph images');
    assert.equal(typeof config.family,'string');
  }
  const delivered=await provider({fallback:{family:'PT Sans',scalars:[0x2928,0x3a9,0x10ffff]}});
  assert.equal(delivered.assignments[0x10ffff],null);
  for (const c of [0x2928,0x3a9]) {
    const name=delivered.assignments[c];
    assert.equal(typeof name,'string');
    const config=JSON.parse(delivered.faces.find(face=>face.font===name).bake);
    assert.equal(config.atlas,undefined,'fallback must only deliver font files');
    raster.call(JSON.stringify({hash:name,font:delivered.fonts[name]}));
    const glyph=JSON.parse(raster.call(JSON.stringify({hash:name,scalar:c,size:14,density:1,flags:config.loaderFlags})));
    assert.equal(glyph.missing,undefined);assert.ok(glyph.width>0 && glyph.height>0);
  }
  console.log('Host font delivery: selected files, fallback coverage including U+2928, absent coverage and local rasterization passed.');
} finally {raster.close();rmSync(cache,{recursive:true,force:true});}
