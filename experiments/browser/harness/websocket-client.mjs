// Raw validation client: browser Origin plus standard masked binary frames.
import net from 'node:net';
import { randomBytes, createHash } from 'node:crypto';

export async function openSocket(url, origin = url) {
  const target = new URL(url);
  const key = randomBytes(16).toString('base64');
  const socket = net.connect(Number(target.port), target.hostname);
  let pending = Buffer.alloc(0);
  let wake;
  let error;
  socket.on('data', data => { pending = Buffer.concat([pending, data]); wake?.(); });
  socket.on('error', e => { error = e; wake?.(); });
  socket.on('close', () => { error ??= new Error('socket closed'); wake?.(); });
  const take = async count => {
    while (pending.length < count) {
      if (error) throw error;
      await new Promise(resolve => { wake = resolve; });
    }
    const data = pending.subarray(0, count);
    pending = pending.subarray(count);
    return data;
  };
  const timer = setTimeout(() => socket.destroy(new Error('WebSocket validation timeout')), 10000);
  socket.once('close', () => clearTimeout(timer));
  socket.write(`GET /host/ws HTTP/1.1\r\nHost: ${target.host}\r\nOrigin: ${origin}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: ${key}\r\nSec-WebSocket-Version: 13\r\nSec-WebSocket-Protocol: slopworld.protobuf.v2\r\n\r\n`);
  let headers = '';
  while (!headers.endsWith('\r\n\r\n')) headers += (await take(1)).toString();
  const expected = createHash('sha1').update(key + '258EAFA5-E914-47DA-95CA-C5AB0DC85B11').digest('base64');
  if (!headers.startsWith('HTTP/1.1 101 ') || !headers.includes(expected)) {
    socket.destroy(); throw new Error(`Upgrade rejected: ${headers.split('\r\n')[0]}`);
  }
  return {
    socket,
    async frame() {
      const prefix = await take(2);
      let length = prefix[1] & 127;
      if (length === 126) length = (await take(2)).readUInt16BE();
      if (length === 127) length = Number((await take(8)).readBigUInt64BE());
      if (length > 32 * 1024 * 1024) throw new Error('Oversized frame');
      return { opcode: prefix[0] & 15, payload: await take(length) };
    },
    send(payload) {
      if (payload.length > 125) throw new Error('Validation sender only supports short messages');
      const mask = randomBytes(4);
      const bytes = Buffer.from(payload);
      for (let i = 0; i < bytes.length; i++) bytes[i] ^= mask[i % 4];
      socket.write(Buffer.concat([Buffer.from([0x82, 0x80 | bytes.length]), mask, bytes]));
    },
  };
}
