import {readFileSync, mkdirSync, writeFileSync} from 'node:fs';
import {execFileSync} from 'node:child_process';
import {resolve} from 'node:path';
import assert from 'node:assert/strict';
import create from '../runtime/wwwroot/local-fonts.mjs';
import {localRasterizer} from '../runtime/wwwroot/workspace-rasterizer.js';

const game = process.env.RIMWORLD ?? `${process.env.HOME}/.local/share/Steam/steamapps/common/RimWorld`;
const out = resolve('experiments/browser/runtime/out/local-font-check');
mkdirSync(out, {recursive:true});
const wasm = await create({wasmBinary:readFileSync(new URL('../runtime/wwwroot/local-fonts.wasm', import.meta.url))});
const local = localRasterizer(wasm);
const scalars = [...'Agyp WéЖΩĄẞⱣḀ€'].map(c => c.codePointAt(0));
writeFileSync(`${out}/scalars.json`, JSON.stringify(scalars));
let compared = 0;
for (const family of ['Fira Sans', 'PT Sans']) {
  const path = execFileSync('fc-match', ['-f','%{file}',family], {encoding:'utf8'});
  const hash = family;
  local.call(JSON.stringify({hash, font:readFileSync(path).toString('base64')}));
  for (const size of [13, 17.25, 17.5]) for (const density of [1, 1.25, 5/3]) for (const style of [0,1,2,3]) {
    const output = `${out}/${family.replaceAll(' ','-')}-${size}-${density}-${style}.json`;
    execFileSync('dotnet', ['experiments/browser/runtime/FontProbe/bin/Debug/net10.0/FontProbe.dll',
      game, path, output, String(size), String(density), String(style), `${out}/scalars.json`],
      {env:{...process.env,LD_LIBRARY_PATH:game}, stdio:'pipe'});
    const native = JSON.parse(readFileSync(output)), pixels = Buffer.from(native.atlas.pixels,'base64');
    // These covered glyphs are outside all three historical bake ranges.
    for (const scalar of [0x104, 0x3a9, 0x1e9e])
      assert.equal(native.glyphs.find(g=>g.scalar===scalar)?.fallback, false);
    const metrics = JSON.parse(local.call(JSON.stringify({hash,size,density,flags:native.loaderFlags})));
    assert.ok(Math.abs(metrics.ascent-native.ascent)<.0001 && Math.abs(metrics.descent-native.descent)<.0001);
    for (const glyph of native.glyphs) {
      const actual = JSON.parse(local.call(JSON.stringify({hash,scalar:glyph.scalar,size,density,flags:native.loaderFlags})));
      const context = `${family} ${size} ${density} ${style} U+${glyph.scalar.toString(16)}`;
      assert.equal(!!actual.missing, glyph.fallback, context);
      if (actual.missing) continue;
      for (const [key, expected] of Object.entries({advance:glyph.advance,...(glyph.visible ? {x:glyph.x0,y:glyph.y0-native.ascent} : {}),ascent:native.ascent,descent:native.descent}))
        assert.ok(Math.abs(actual[key]-expected) < .0001, `${context} ${key}: ${actual[key]} != ${expected}`);
      const alpha = Buffer.from(actual.pixels,'base64');
      const left = Math.round(glyph.u0*native.atlas.width), top = Math.round(glyph.v0*native.atlas.height);
      assert.equal(actual.width, Math.round((glyph.u1-glyph.u0)*native.atlas.width), context+' width');
      assert.equal(actual.height, Math.round((glyph.v1-glyph.v0)*native.atlas.height), context+' height');
      for (let y=0;y<actual.height;y++) for(let x=0;x<actual.width;x++) {
        const offset = ((top+y)*native.atlas.width+left+x)*native.atlas.bytesPerPixel;
        assert.equal(alpha[y*actual.width+x],pixels[offset+(native.atlas.bytesPerPixel===4?3:0)],context+' alpha');
      }
      compared++;
    }
  }
}
local.close(); local.close();
assert.throws(()=>local.call(JSON.stringify({hash:'Fira Sans',scalar:65,size:15,density:1,flags:513})),/not loaded/);
assert.throws(()=>local.call(JSON.stringify({hash:'bad',font:Buffer.from('bad').toString('base64')})),/Invalid/);
console.log(`Compared ${compared} local WASM glyphs with native FontProbe: metrics and alpha pixels identical`);
