// Close only the Chromium instance identified by the experiment's live profile.
import { readFile } from 'node:fs/promises';
const [port, endpoint] = (await readFile(`${process.argv[2]}/DevToolsActivePort`, 'utf8')).trim().split('\n');
const socket = new WebSocket(`ws://127.0.0.1:${port}${endpoint}`);
await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
socket.send(JSON.stringify({ id: 1, method: 'Browser.close' }));
await new Promise(resolve => { socket.onclose = resolve; });
