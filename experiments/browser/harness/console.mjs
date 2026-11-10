// Read buffered console evidence from an already-running local probe browser.
import { readFile } from 'node:fs/promises';
import { setTimeout as delay } from 'node:timers/promises';
const [profile] = process.argv.slice(2);
const port = (await readFile(`${profile}/DevToolsActivePort`, 'utf8')).split('\n')[0];
const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
const socket = new WebSocket(targets.find(target => target.type === 'page' && target.url.startsWith('http://127.0.0.1:')).webSocketDebuggerUrl);
await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
const events = [];
socket.onmessage = ({ data }) => {
  const message = JSON.parse(data);
  if (message.method === 'Runtime.consoleAPICalled') events.push({ type: message.params.type, text: message.params.args.map(arg => arg.value ?? arg.description).join(' ') });
};
socket.send(JSON.stringify({ id: 1, method: 'Runtime.enable' }));
await delay(1500);
socket.close();
console.log(JSON.stringify({ count: events.length, recent: events.slice(-20) }, null, 2));
