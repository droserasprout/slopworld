import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { createServer } from 'node:http';
import os from 'node:os';
import path from 'node:path';
import { endpointProvider } from '../../server/endpoint.mjs';
import { serve } from '../../server/server.mjs';

test('running bridge reloads complete credentials and fails closed until descriptor recovers', async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'endpoint-'));
  const descriptor = path.join(root, 'endpoint.toml');
  const seen = [];
  const upstream = createServer((req, res) => {
    seen.push(req.headers['x-slop-token']);
    res.end('ok');
  });
  await new Promise(resolve => upstream.listen(0, '127.0.0.1', resolve));
  const address = `http://127.0.0.1:${upstream.address().port}`;
  const provider = endpointProvider({ ...process.env, SLOPWEB_ENDPOINT_FILE: descriptor });
  const { server, url } = await serve(root, 0, provider);
  const request = () => fetch(url + '/host/health', { headers: { origin: url } });
  try {
    for (const token of ['first', 'rotated']) {
      await writeFile(descriptor, `url = "${address}"\ntoken = "${token}"\n`);
      assert.equal((await request()).status, 200);
    }
    await writeFile(descriptor, 'invalid TOML');
    assert.equal((await request()).status, 503);
    await rm(descriptor);
    assert.equal((await request()).status, 503);
    await writeFile(descriptor, `url = "${address}"\ntoken = "recovered"\n`);
    assert.equal((await request()).status, 200);
    assert.deepEqual(seen, ['first', 'rotated', 'recovered']);
  } finally {
    server.closeAllConnections();
    upstream.closeAllConnections();
    await Promise.all([new Promise(resolve => server.close(resolve)), new Promise(resolve => upstream.close(resolve))]);
    await rm(root, { recursive: true, force: true });
  }
});
