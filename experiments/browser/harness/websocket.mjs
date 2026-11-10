// Preserve native WebSocket framing and stream backpressure across a host-authenticated
// upgrade. The daemon owns protobuf, subscriptions, and command authorization.
import http from 'node:http';
import https from 'node:https';

export function daemonSocket(req, socket, head, daemon) {
  const reject = (status = 403) => socket.end(`HTTP/1.1 ${status} Rejected\r\nConnection: close\r\nContent-Length: 0\r\n\r\n`);
  if (req.url !== '/host/ws' || req.method !== 'GET' ||
      !/^127\.0\.0\.1:\d+$/.test(req.headers.host ?? '') ||
      req.headers.origin !== `http://${req.headers.host}` ||
      req.headers['sec-websocket-protocol'] !== 'slopworld.protobuf.v2' ||
      req.headers['sec-websocket-version'] !== '13' ||
      !/^[A-Za-z0-9+/]{22}==$/.test(req.headers['sec-websocket-key'] ?? '')) {
    reject(); return;
  }
  if (!daemon.endpoint || !daemon.token) { reject(503); return; }
  let upstream;
  let peer;
  socket.on('error', () => { upstream?.destroy(); peer?.destroy(); });
  socket.on('close', () => { upstream?.destroy(); peer?.destroy(); });
  socket.on('end', () => { upstream?.destroy(); peer?.destroy(); socket.destroy(); });
  try {
    const target = new URL('/ws', daemon.endpoint);
    const transport = target.protocol === 'https:' ? https : http;
    upstream = transport.request(target, { headers: {
      Connection: 'Upgrade', Upgrade: 'websocket',
      'sec-websocket-version': '13',
      'sec-websocket-key': req.headers['sec-websocket-key'],
      'sec-websocket-protocol': 'slopworld.protobuf.v2',
      'x-slop-token': daemon.token,
    } });
    upstream.setTimeout(5000, () => upstream.destroy());
    upstream.on('error', () => { if (!peer) reject(502); else socket.destroy(); });
    upstream.on('response', response => { response.resume(); reject(502); });
    upstream.on('upgrade', (response, remote, remoteHead) => {
      peer = remote;
      remote.setTimeout(0);
      if (response.headers['sec-websocket-protocol'] !== 'slopworld.protobuf.v2' ||
          !/^[A-Za-z0-9+/]{27}=$/.test(response.headers['sec-websocket-accept'] ?? '')) {
        remote.destroy(); reject(502); return;
      }
      remote.on('error', () => socket.destroy());
      remote.on('close', () => socket.destroy());
      remote.on('end', () => socket.destroy());
      socket.write('HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n' +
        `Sec-WebSocket-Accept: ${response.headers['sec-websocket-accept']}\r\n` +
        'Sec-WebSocket-Protocol: slopworld.protobuf.v2\r\n\r\n');
      if (remoteHead.length) socket.write(remoteHead);
      if (head.length) remote.write(head);
      socket.pipe(remote).pipe(socket);
    });
    upstream.end();
  } catch { upstream?.destroy(); reject(502); }
}
