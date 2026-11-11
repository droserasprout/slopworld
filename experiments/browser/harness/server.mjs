// Loopback-only asset server. The selected build directory is its entire authority.
import http from 'node:http';
import { fontProvider, fontHttp } from './fonts.mjs';
import { daemonHttp } from './bridge.mjs';
import { daemonSocket } from './websocket.mjs';
import { realpath, readFile, stat } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

export async function serve(directory, port = 0, daemon = { endpoint: process.env.SLOPD_URL, token: process.env.SLOPD_TOKEN }, fonts = null) {
  const root = await realpath(directory);
  const types = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript',
    '.json': 'application/json', '.wasm': 'application/wasm', '.css': 'text/css' };
  const server = http.createServer(async (req, res) => {
    res.setHeader('Cross-Origin-Opener-Policy', 'same-origin');
    res.setHeader('Cross-Origin-Embedder-Policy', 'require-corp');
    res.setHeader('Cache-Control', 'no-store');
    if (!/^127\.0\.0\.1:\d+$/.test(req.headers.host ?? '')) {
      res.writeHead(403).end(); return;
    }
    if (req.url === '/host/fonts') {
      await fontHttp(req, res, fonts); return;
    }
    if (req.url === '/host/health' || req.url.startsWith('/host/api/')) {
      await daemonHttp(req, res, daemon.endpoint, daemon.token);
      return;
    }
    if (!['GET', 'HEAD'].includes(req.method)) {
      res.writeHead(403).end(); return;
    }
    try {
      let relative = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
      if (relative.endsWith('/')) relative += 'index.html';
      const file = await realpath(path.resolve(root, '.' + relative));
      if (!file.startsWith(root + path.sep) || !(await stat(file)).isFile()) {
        res.writeHead(403).end(); return;
      }
      const data = await readFile(file);
      res.writeHead(200, { 'Content-Type': types[path.extname(file)] ?? 'application/octet-stream',
        'Content-Length': data.length });
      res.end(req.method === 'HEAD' ? undefined : data);
    } catch { res.writeHead(404).end(); }
  });
  server.on('upgrade', (req, socket, head) => daemonSocket(req, socket, head, daemon));
  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(port, '127.0.0.1', resolve);
  });
  return { server, url: `http://127.0.0.1:${server.address().port}` };
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  const fonts = process.env.SLOPWEB_FONT_GAME ? fontProvider({
    helper: path.resolve(path.dirname(process.argv[1]), '../fonts.py'),
    game: process.env.SLOPWEB_FONT_GAME, cache: process.env.SLOPWEB_FONT_CACHE,
  }) : null;
  const { url } = await serve(process.argv[2], Number(process.argv[3] ?? 8787), undefined, fonts);
  console.log(url);
}
