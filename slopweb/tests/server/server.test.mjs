import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, writeFile, symlink, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { sameOrigin } from '../../server/bridge.mjs';
import { request } from 'node:http';
import { serve } from '../../server/server.mjs';

test('local assets are isolated and credentialed health rejects foreign requests', async () => {
  const temporary = await mkdtemp(path.join(os.tmpdir(), 'wasm-server-test-'));
  const { server, url } = await serve(temporary);
  try {
    await writeFile(path.join(temporary, 'index.html'), '<h1>probe</h1>');
    await symlink('/etc/passwd', path.join(temporary, 'escape'));
    assert.equal(server.address().address, '127.0.0.1');
    assert.equal((await remoteFetch(url, { headers: { host: 'evil.invalid' } })).status, 403);
    const page = await fetch(url);
    assert.equal(await page.text(), '<h1>probe</h1>');
    assert.equal(page.headers.get('cross-origin-embedder-policy'), 'require-corp');
    assert.equal((await fetch(url + '/escape')).status, 403);
    assert.equal((await fetch(url + '/host/health')).status, 403);
    assert.equal((await fetch(url, { method: 'POST' })).status, 403);
    assert.equal((await fetch(url + '/missing')).status, 404);
    const data = Buffer.alloc(2 * 1024 * 1024, 37);
    await writeFile(path.join(temporary, 'large.wasm'), data);
    const head = await fetch(url + '/large.wasm', { method: 'HEAD' });
    assert.equal(head.status, 200);
    assert.equal(head.headers.get('content-length'), String(data.length));
    assert.equal((await head.arrayBuffer()).byteLength, 0);
    const download = await fetch(url + '/large.wasm');
    assert.deepEqual(Buffer.from(await download.arrayBuffer()), data);
    // Disconnect mid-stream; subsequent requests must still work.
    const aborted = await fetch(url + '/large.wasm');
    await aborted.body.cancel();
    assert.equal((await fetch(url)).status, 200);
  } finally {
    server.closeAllConnections();
    await new Promise(resolve => server.close(resolve));
    await rm(temporary, { recursive: true, force: true });
  }
});

test('HTTP bridge preserves protobuf and status, supplies only host credentials, and rejects foreign mutations', async () => {
  const { createServer } = await import('node:http');
  const seen = [];
  const upstream = createServer(async (req, res) => {
    const chunks = [];
    for await (const chunk of req) chunks.push(chunk);
    seen.push({ url: req.url, method: req.method, headers: req.headers, body: Buffer.concat(chunks) });
    res.writeHead(409, { 'x-slop-token': 'must-not-forward' }).end(Buffer.from([8, 1, 255]));
  });
  await new Promise(resolve => upstream.listen(0, '127.0.0.1', resolve));
  const temporary = await mkdtemp(path.join(os.tmpdir(), 'wasm-bridge-test-'));
  const { server, url } = await serve(temporary, 0, {
    endpoint: `http://127.0.0.1:${upstream.address().port}`, token: 'host-secret',
  });
  try {
    for (const headers of [{}, { 'sec-fetch-site': 'cross-site' },
      { 'sec-fetch-site': 'same-origin', origin: 'http://evil.invalid' }]) {
      assert.equal((await fetch(url + '/host/api/sessions', { method: 'POST', headers })).status, 403);
    }
    assert.equal(seen.length, 0);
    assert.equal((await remoteFetch(url + '/host/api/sessions', {
      method: 'POST', headers: { host: 'evil.invalid', origin: 'http://evil.invalid' },
    })).status, 403);
    assert.equal(seen.length, 0);
    const response = await remoteFetch(url + '/host/api/sessions', {
      method: 'POST', body: Buffer.from([10, 2, 97, 98]),
      headers: { host: new URL(url).host, origin: url, 'x-slop-token': 'browser-secret', cookie: 'private=1' },
    });
    assert.equal(response.status, 409);
    assert.deepEqual(Buffer.from(await response.arrayBuffer()), Buffer.from([8, 1, 255]));
    assert.equal(response.headers.get('x-slop-token'), null);
    assert.equal(seen[0].url, '/api/sessions');
    assert.equal(seen[0].method, 'POST');
    assert.equal(seen[0].headers['x-slop-token'], 'host-secret');
    assert.equal(seen[0].headers.cookie, undefined);
    assert.deepEqual(seen[0].body, Buffer.from([10, 2, 97, 98]));
    assert.equal((await fetch(url + '/host/api/health?token=bad', {
      headers: { 'sec-fetch-site': 'same-origin' },
    })).status, 400);
    assert.equal(seen.length, 1);
  } finally {
    server.closeAllConnections(); upstream.closeAllConnections();
    await Promise.all([new Promise(resolve => server.close(resolve)), new Promise(resolve => upstream.close(resolve))]);
    await rm(temporary, { recursive: true, force: true });
  }
});

test('WebSocket bridge rejects foreign origins and relays masked binary traffic with host authentication', async () => {
  const { createServer } = await import('node:http');
  const { createHash } = await import('node:crypto');
  const { openSocket } = await import('./websocket-client.mjs');
  let upgrades = 0;
  let remote;
  const upstream = createServer();
  upstream.on('upgrade', (req, socket) => {
    upgrades++;
    remote = socket;
    socket.on('end', () => socket.destroy());
    assert.equal(req.url, '/ws');
    assert.equal(req.headers['x-slop-token'], 'host-secret');
    assert.equal(req.headers.origin, undefined);
    const accept = createHash('sha1').update(req.headers['sec-websocket-key'] +
      '258EAFA5-E914-47DA-95CA-C5AB0DC85B11').digest('base64');
    socket.write(`HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: ${accept}\r\nSec-WebSocket-Protocol: slopworld.protobuf.v2\r\n\r\n`);
    let received = Buffer.alloc(0);
    socket.on('data', chunk => {
      received = Buffer.concat([received, chunk]);
      if (received.length < 2) return;
      const length = received[1] & 127;
      if (received.length < length + 6) return;
      assert.equal(received[0], 0x82);
      assert.equal(received[1] & 128, 128);
      const data = Buffer.from(received.subarray(6, 6 + length));
      for (let i = 0; i < length; i++) data[i] ^= received[2 + i % 4];
      socket.write(Buffer.concat([Buffer.from([0x82, length]), data]));
      received = received.subarray(6 + length);
    });
  });
  await new Promise(resolve => upstream.listen(0, '127.0.0.1', resolve));
  const temporary = await mkdtemp(path.join(os.tmpdir(), 'wasm-socket-test-'));
  const { server, url } = await serve(temporary, 0, {
    endpoint: `http://127.0.0.1:${upstream.address().port}`, token: 'host-secret',
  });
  let client;
  try {
    await assert.rejects(openSocket(url, 'http://evil.invalid'), /403/);
    assert.equal(upgrades, 0);
    await assert.rejects(openSocket(url, 'http://evil.invalid', 'evil.invalid'), /403/);
    assert.equal(upgrades, 0);
    client = await openSocket(url, url, new URL(url).host);
    const data = Buffer.from([10, 0, 255, 128]);
    client.send(data);
    const frame = await client.frame();
    assert.equal(frame.opcode, 2);
    assert.deepEqual(frame.payload, data);
    assert.equal(upgrades, 1);
    const closed = new Promise(resolve => remote.once('close', resolve));
    client.socket.destroy();
    await closed;
  } finally {
    client?.socket.destroy(); remote?.destroy();
    server.closeAllConnections(); upstream.closeAllConnections();
    await Promise.all([new Promise(resolve => server.close(resolve)), new Promise(resolve => upstream.close(resolve))]);
    await rm(temporary, { recursive: true, force: true });
  }
});

test('plain HTTP provenance accepts same-origin requests and rejects foreign or missing provenance', () => {
  const host = 'g14.tail86ccc.ts.net:8787';
  const check = headers => sameOrigin({ headers: { host, ...headers } });
  assert.equal(check({ referer: `http://${host}/workspace/` }), true);
  assert.equal(check({ origin: `http://${host}` }), true);
  assert.equal(check({}), false);
  assert.equal(check({ referer: 'http://evil.invalid/' }), false);
  assert.equal(check({ referer: 'invalid' }), false);
  assert.equal(check({ origin: 'null', referer: `http://${host}/` }), false);
  assert.equal(check({ 'sec-fetch-site': 'cross-site', origin: `http://${host}` }), false);
});

// Node fetch replaces Host; use HTTP directly to exercise Host validation.
function remoteFetch(url, options) {
  return new Promise((resolve, reject) => {
    const req = request(url, options, async res => {
      const chunks = [];
      for await (const chunk of res) chunks.push(chunk);
      resolve(new Response(Buffer.concat(chunks), { status: res.statusCode, headers: res.headers }));
    });
    req.on('error', reject);
    req.end(options.body);
  });
}
