// Loopback asset server and credentialed bridge; remote access needs an authenticated proxy.
import http from 'node:http';
import { endpointProvider } from './endpoint.mjs';
import { fontProvider, fontHttp } from './fonts.mjs';
import { daemonHttp } from './bridge.mjs';
import { daemonSocket } from './websocket.mjs';
import { realpath, open } from 'node:fs/promises';
import { pipeline } from 'node:stream/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

export async function serve(directory, port = 0, daemon = endpointProvider(), fonts = null, publicOrigin = null) {
  if (publicOrigin !== null) {
    const parsed = new URL(publicOrigin);
    if (parsed.protocol !== 'https:' || parsed.origin !== publicOrigin)
      throw new Error('Public origin must be an exact HTTPS origin without a path or credentials');
  }
  const root = await realpath(directory);
  const resolveDaemon = typeof daemon === 'function' ? daemon : async () => daemon;
  const types = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript',
    '.json': 'application/json', '.wasm': 'application/wasm', '.css': 'text/css' };
  const server = http.createServer(async (req, res) => {
    if (!localHost(req)) { res.writeHead(403).end(); return; }
    res.setHeader('Cross-Origin-Opener-Policy', 'same-origin');
    res.setHeader('Cross-Origin-Embedder-Policy', 'require-corp');
    res.setHeader('Cache-Control', 'no-store');
    if (req.url === '/host/fonts') {
      await fontHttp(req, res, fonts, publicOrigin ?? undefined); return;
    }
    if (req.url === '/host/health' || req.url.startsWith('/host/api/')) {
      const snapshot = await resolveDaemon();
      await daemonHttp(req, res, snapshot.endpoint, snapshot.token, publicOrigin ?? undefined);
      return;
    }
    if (!['GET', 'HEAD'].includes(req.method)) {
      res.writeHead(403).end(); return;
    }
    let handle;
    try {
      let relative = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
      if (relative.endsWith('/')) relative += 'index.html';
      const file = await realpath(path.resolve(root, '.' + relative));
      if (!file.startsWith(root + path.sep)) {
        res.writeHead(403).end(); return;
      }
      handle = await open(file);
      const metadata = await handle.stat();
      if (!metadata.isFile()) { res.writeHead(403).end(); return; }
      res.writeHead(200, { 'Content-Type': types[path.extname(file)] ?? 'application/octet-stream',
        'Content-Length': metadata.size });
      if (req.method === 'HEAD') res.end();
      else await pipeline(handle.createReadStream({ autoClose: false }), res);
    } catch {
      if (res.headersSent || res.destroyed) res.destroy();
      else res.writeHead(404).end();
    } finally { await handle?.close(); }
  });
  // Validate Host independently of Origin to reject DNS rebinding. Header
  // provenance is CSRF protection, not authentication for remote clients.
  function localHost(req) {
    const port = server.address().port;
    return [`127.0.0.1:${port}`, `localhost:${port}`,
      ...(publicOrigin ? [new URL(publicOrigin).host] : [])].includes(req.headers.host);
  }
  server.on('upgrade', async (req, socket, head) => {
    if (!localHost(req)) {
      socket.end('HTTP/1.1 403 Forbidden\r\nConnection: close\r\nContent-Length: 0\r\n\r\n');
      return;
    }
    const snapshot = await resolveDaemon();
    if (!socket.destroyed) daemonSocket(req, socket, head, snapshot, publicOrigin ?? undefined);
  });
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
  const { url } = await serve(process.argv[2], Number(process.argv[3] ?? 8787), undefined, fonts, process.env.SLOPWEB_PUBLIC_ORIGIN ?? null);
  console.log(url);
}
