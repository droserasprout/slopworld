// Running Rust host -> immutable font files -> local FreeType, without a browser.
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import create from '../../../runtime/wwwroot/local-fonts.mjs';
import {localRasterizer} from '../../../runtime/wwwroot/workspace-rasterizer.js';
import {covers} from '../../../runtime/wwwroot/host-fonts.js';
const origin=new URL(process.argv[2]??'http://127.0.0.1:8787').origin;
const request=path=>fetch(origin+path,{headers:{origin,'sec-fetch-site':'same-origin'}});
const raster=localRasterizer(await create({wasmBinary:readFileSync(new URL('../../../runtime/wwwroot/local-fonts.wasm',import.meta.url))}));
try {
  const response=await request('/host/fonts/catalog?family=PT%20Sans');
  assert.equal(response.status,200);
  const catalog=await response.json();
  assert.equal(catalog.version,1);
  const faces=new Map(catalog.faces.map(face=>[face.hash,face]));
  for (const scalar of [0x2928,0x3a9]) {
    const hash=catalog.order.find(hash=>covers(faces.get(hash).coverage,scalar));
    assert.ok(hash,`Installed fonts must cover U+${scalar.toString(16)}`);
    const font=await request('/host/fonts/files/'+hash);
    assert.equal(font.status,200);
    const bytes=Buffer.from(await font.arrayBuffer());
    raster.call(JSON.stringify({hash,font:bytes.toString('base64')}));
    const glyph=JSON.parse(raster.call(JSON.stringify({hash,scalar,size:14,density:1,flags:faces.get(hash).loaderFlags})));
    assert.equal(glyph.missing,undefined);assert.ok(glyph.width>0&&glyph.height>0);
  }
  console.log('Rust host font coverage, immutable file delivery and local rasterization passed.');
} finally {raster.close();}
