// Exercise the actual configured daemon through the browser-facing HTTP boundary.
import assert from 'node:assert/strict';
import { serve } from './server.mjs';
import { openSocket } from './websocket-client.mjs';

const { server, url } = await serve('experiments/browser/app');
try {
  for (const route of ['health', 'sessions']) {
    const response = await fetch(`${url}/host/api/${route}`, {
      headers: { 'sec-fetch-site': 'same-origin', origin: url },
    });
    assert.equal(response.status, 200, `${route}: HTTP ${response.status}`);
    const bytes = new Uint8Array(await response.arrayBuffer());
    console.log(`${route}: HTTP 200, ${bytes.length} protobuf bytes`);
  }
  // A read-only root route checks the host credential's scope without creating
  // sessions or exporting configuration/credentials into the report.
  const access = await fetch(`${url}/host/api/templates`, {
    headers: { 'sec-fetch-site': 'same-origin', origin: url },
  });
  await access.arrayBuffer();
  assert.ok([200,403].includes(access.status), `root access check: HTTP ${access.status}`);
  console.log(`Root-only workspace routes: ${access.status === 200 ? 'authorized' : 'restricted by host token (HTTP 403)'}`);
  if (process.env.REQUIRE_WORKSPACE_WRITE_ACCESS === '1') assert.equal(access.status,200,'Workspace session creation needs a root-token host connection');
  const ws = await openSocket(url);
  try {
    const frame = await ws.frame();
    assert.equal(frame.opcode, 2);
    assert.ok(frame.payload.length > 0);
    console.log(`WebSocket: negotiated v2, ${frame.payload.length} binary snapshot bytes`);
  } finally { ws.socket.destroy(); }
} finally {
  server.closeAllConnections();
  await new Promise(resolve => server.close(resolve));
}
