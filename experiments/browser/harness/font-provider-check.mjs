// Real host font-provider smoke check. No browser, renderer, or game startup.
import {mkdtemp, rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {createHash} from 'node:crypto';
import assert from 'node:assert/strict';
import {fontProvider} from '../../../slopweb/server/fonts.mjs';
const [bundle, game, family='Fira Sans', density='1.25'] = process.argv.slice(2);
const cache=await mkdtemp(path.join(tmpdir(),'slopweb-font-provider-'));
try {
  const provider=fontProvider({helper:path.resolve(bundle,'fonts.py'),game,cache});
  const result=await provider({uiFontName:family,density:Number(density)});
  if(result.faces.length!==4)throw Error('Selection did not return the four native metric faces');
  const hash=createHash('sha256').update(Buffer.from(result.fonts['ui.ttf'],'base64')).digest('hex').toUpperCase();
  const scalars=[...new Set(Array.from('Font', c=>c.codePointAt(0)))].sort((a,b)=>a-b);
  await assert.rejects(provider({face:{hash,size:14,density:Number(density),style:0,scalars}}), /not supported/);
  for (const face of result.faces) {
    const config = JSON.parse(face.bake);
    assert.ok(config.size > 0);
    assert.equal(config.loaderFlags, 513);
    assert.equal(config.atlas, undefined);
  }
  console.log(JSON.stringify({family,faces:result.faces.length,hostRasterization:false}));
} finally { await rm(cache,{recursive:true,force:true}); }
