// Host-only font preparation: one subprocess, bounded queue/cache, no shell.
import { execFile } from 'node:child_process';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { promisify } from 'node:util';
const execute = promisify(execFile);

export function fontSelection(value) {
  if (value.face) throw Error('Host glyph rasterization is not supported');
  const result = {};
  for (const [key, fallback] of [['uiFontName', ''], ['fontName', 'monospace']]) {
    const text = value[key] ?? fallback;
    if (typeof text !== 'string' || text.length > 256 || /[\x00-\x1f]/.test(text)) throw Error('Invalid font family');
    result[key] = text;
  }
  for (const [key, fallback, low, high] of [['uiFontSize',14,0,48], ['fontSize',14,8,28]]) {
    const size = value[key] ?? fallback;
    if (!Number.isInteger(size) || size < low || size > high) throw Error('Invalid font size');
    result[key] = size;
  }
  const density = value.density ?? 1;
  if (!Number.isFinite(density) || density < .5 || density > 4) throw Error('Invalid font density');
  result.density = density;
  return result;
}

export function fontProvider({helper, game, cache}, run = execute, now = Date.now) {
  const requests = new Map();
  let active = false;
  return async (value, retry = false) => {
    const selected = fontSelection(value), key = JSON.stringify(selected);
    if (requests.has(key)) {
      const saved = requests.get(key);
      if (!saved.failedAt || (!retry && now() - saved.failedAt < 30000)) return saved.promise;
      requests.delete(key);
    }
    if (active) throw Error('Font preparation busy');
    active = true;
    const pending = (async () => {
      const {stdout} = await run('python3', [helper, game, cache, key], {timeout: 180000, maxBuffer: 4096});
      const root = stdout.trim();
      if (path.dirname(root) !== path.resolve(cache) || !/^[a-f0-9]{64}$/.test(path.basename(root))) throw Error('Invalid font cache');
      const manifest = JSON.parse(await readFile(path.join(root, 'manifest.json')));
      if (!Array.isArray(manifest) || manifest.length > 1024) throw Error('Invalid font manifest');
      const fonts = {};
      let bytes = 0;
      const faces = [];
      for (const face of manifest) {
        if (!/^(ui|terminal|face)\.ttf$/.test(face.font) || !/^[\w.-]+\.json$/.test(face.bake)) throw Error('Invalid font manifest');
        if (!fonts[face.font]) {
          fonts[face.font] = (await readFile(path.join(root, face.font))).toString('base64');
          bytes += fonts[face.font].length;
        }
        const bake = await readFile(path.join(root, face.bake), 'utf8');
        bytes += bake.length;
        if (bytes > 64 * 1024 * 1024) throw Error('Font response too large');
        faces.push({font: face.font, bake});
      }
      return {fonts, faces, chains: JSON.parse(await readFile(path.join(root, 'chains.json')))};
    })();
    const entry = {promise:pending, failedAt:0};
    requests.set(key, entry);
    // The host helper bounds disk entries independently. This response cache
    // retains the eight most recent results, including failures.
    try { return await pending; }
    catch (error) { entry.failedAt = now(); throw error; }
    finally { active = false; if (requests.size > 8) requests.delete(requests.keys().next().value); }
  };
}

export async function fontHttp(req, res, provider) {
  if (!provider) { res.writeHead(404).end(); return; }
  if (req.method !== 'POST' || req.headers.origin !== `http://${req.headers.host}` || req.headers['sec-fetch-site'] !== 'same-origin') {
    res.writeHead(403).end(); return;
  }
  try {
    let body = '';
    for await (const chunk of req) { body += chunk; if (body.length > 512 * 1024) throw Error('Font request too large'); }
    const value = fontSelection(JSON.parse(body));
    const result = await provider(value, req.headers['x-slop-font-retry'] === '1');
    res.writeHead(200, {'Content-Type':'application/json'}).end(JSON.stringify(result));
  } catch { res.writeHead(422).end('Font preparation unavailable'); }
}
