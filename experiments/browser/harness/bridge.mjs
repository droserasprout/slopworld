// Host-only HTTP boundary. Browser callers supply protobuf, never daemon credentials.
// WebSocket adaptation and the managed mod client remain separate owners.
export async function daemonHttp(req, res, endpoint, token) {
  const origin = `http://${req.headers.host}`;
  if (req.headers['sec-fetch-site'] !== 'same-origin' ||
      (req.headers.origin && req.headers.origin !== origin) ||
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
      redirect: 'error', signal: AbortSignal.timeout(30000),
    });
    const data = Buffer.from(await response.arrayBuffer());
    res.writeHead(response.status, { 'Content-Type': 'application/x-protobuf' });
    res.end(req.method === 'HEAD' ? undefined : data);
  } catch { res.writeHead(502).end('Host daemon unavailable.'); }
}
