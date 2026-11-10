// Run a local diagnostic script in an already-loaded local browser workspace.
import { readFile } from 'node:fs/promises';
const [profile, script] = process.argv.slice(2);
const port = (await readFile(`${profile}/DevToolsActivePort`, 'utf8')).split('\n')[0];
const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
const page = targets.find(t => t.type === 'page' && t.url.startsWith('http://127.0.0.1:'));
if (!page) throw new Error('No live local workspace page');
const socket = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
try {
  const expression = await readFile(script, 'utf8');
  const result = await new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error('Browser evaluation timed out; runtime may still be working')), Number(process.env.PROBE_EVALUATE_TIMEOUT_MS ?? 30000));
    socket.onmessage = ({ data }) => {
      const message = JSON.parse(data);
      if (message.id !== 1) return;
      clearTimeout(timeout);
      if (message.error) reject(new Error(JSON.stringify(message.error)));
      else resolve(message.result);
    };
    socket.send(JSON.stringify({ id: 1, method: 'Runtime.evaluate', params: { expression, awaitPromise: true, returnByValue: true } }));
  });
  console.log(JSON.stringify(result, null, 2));
  if (result.exceptionDetails) process.exitCode = 1;
} finally { socket.close(); }
