// Capture an existing local diagnostic page without restarting its runtime.
import {readFile, writeFile} from 'node:fs/promises';
const [profile, output] = process.argv.slice(2);
if (!profile || !output) throw new Error('Usage: screenshot.mjs PROFILE OUTPUT.png');
const port = (await readFile(`${profile}/DevToolsActivePort`, 'utf8')).split('\n')[0];
const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
const page = targets.find(t => t.type === 'page' && t.url.startsWith('http://127.0.0.1:'));
if (!page) throw new Error('No local diagnostic page');
const socket = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((resolve,reject) => { socket.onopen=resolve; socket.onerror=reject; });
try {
  const response = await new Promise((resolve,reject) => {
    const timer=setTimeout(()=>reject(new Error('Screenshot timed out')),30000);
    socket.onmessage=({data})=>{
      const message=JSON.parse(data); if(message.id!==1)return;
      clearTimeout(timer); if(message.error)reject(new Error(JSON.stringify(message.error)));else resolve(message.result);
    };
    socket.send(JSON.stringify({id:1,method:'Page.captureScreenshot',params:{format:'png'}}));
  });
  await writeFile(output,Buffer.from(response.data,'base64'));
} finally {socket.close();}
