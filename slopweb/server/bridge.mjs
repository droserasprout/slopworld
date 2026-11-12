// Host-only HTTP boundary. Browser callers supply protobuf, never daemon credentials.
// WebSocket adaptation and the managed mod client remain separate owners.
// Plain HTTP browsers omit Fetch Metadata. Require an explicit matching Origin
// or Referer in that case; never treat missing browser provenance as same-origin.
export function sameOrigin(req, expected = `http://${req.headers.host}`) {
  const site = req.headers['sec-fetch-site'];
  if (site && site !== 'same-origin') return false;
  if (req.headers.origin) return req.headers.origin === expected;
  if (req.headers.referer) {
    try { return new URL(req.headers.referer).origin === expected; }
    catch { return false; }
  }
  return site === 'same-origin';
}

export async function daemonHttp(req, res, endpoint, token, origin) {
  if (!sameOrigin(req, origin) ||
      !['GET', 'HEAD', 'POST', 'PUT', 'DELETE', 'PATCH'].includes(req.method)) {
    res.writeHead(403).end(); return;
  }
  if (!endpoint || !token) {
    res.writeHead(503).end('Configure the host daemon endpoint.'); return;
  }
  const route = req.url === '/host/health' ? '/api/health' : req.url.slice('/host'.length);
  // Disallow query credentials and URL normalization that could escape /api/.
  if (!/^\/api\/[A-Za-z0-9_/%.-]+(?:\?[^#]*)?$/.test(route) ||
      /(?:token|credential|authorization)=/i.test(route)) {
    res.writeHead(400).end(); return;
  }
  // The managed transport owns operation deadlines (including 60-second worker
  // spawns). A browser disconnect cancels upstream I/O, without replaying mutations.
  const controller = new AbortController();
  const cancel = () => controller.abort();
  req.once('aborted', cancel);
  res.once('close', cancel);
  try {
    const target = new URL(route, endpoint);
    if (!target.pathname.startsWith('/api/')) {
      res.writeHead(400).end(); return;
    }
    const chunks = [];
    let size = 0;
    for await (const chunk of req) {
      size += chunk.length;
      if (size > 32 * 1024 * 1024) {
        res.writeHead(413).end(); return;
      }
      chunks.push(chunk);
    }
    const response = await fetch(target, {
      method: req.method,
      headers: { 'x-slop-token': token, 'content-type': 'application/x-protobuf' },
      body: ['GET', 'HEAD'].includes(req.method) ? undefined : Buffer.concat(chunks),
      redirect: 'error', signal: controller.signal,
    });
    const data = Buffer.from(await response.arrayBuffer());
    res.writeHead(response.status, { 'Content-Type': 'application/x-protobuf' });
    res.end(req.method === 'HEAD' ? undefined : data);
  } catch { if (!res.destroyed) res.writeHead(502).end('Host daemon unavailable.'); }
  finally { req.off('aborted', cancel); res.off('close', cancel); }
}
