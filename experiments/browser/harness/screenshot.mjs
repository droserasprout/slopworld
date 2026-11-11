// Capture an existing local diagnostic page without restarting its runtime.
import {readFile, writeFile} from 'node:fs/promises';
const [profile, output, mode] = process.argv.slice(2);
if (mode && mode !== 'canvas') throw new Error('Unknown screenshot mode');
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
    socket.send(JSON.stringify(mode === 'canvas'
      ? {id:1,method:'Runtime.evaluate',params:{expression:`(() => {
          const canvas = document.querySelector('canvas');
          if (!canvas || window.__workspace?.status !== 'running') throw new Error('No running workspace canvas');
          const context=canvas.getContext('2d');
          // Float16 canvas PNG export can truncate differently from RGBA8 readback.
          // Capture the explicit byte readback, without resizing or changing the live canvas.
          const pixels=context.getImageData(0,0,canvas.width,canvas.height);
          const copy=document.createElement('canvas');copy.width=canvas.width;copy.height=canvas.height;
          copy.getContext('2d').putImageData(pixels,0,0);
          return {data:copy.toDataURL('image/png').split(',')[1],
            width:canvas.width,height:canvas.height,context:context.getContextAttributes(),
            capture:'RGBA8 getImageData, unscaled PNG'};
        })()`,returnByValue:true}}
      : {id:1,method:'Page.captureScreenshot',params:{format:'png'}}));
  });
  if (response.exceptionDetails) throw new Error(JSON.stringify(response.exceptionDetails));
  const capture=mode === 'canvas' ? response.result.value : response;
  await writeFile(output,Buffer.from(capture.data,'base64'));
  if(mode==='canvas') {
    const {data,...metadata}=capture;
    await writeFile(output+'.json',JSON.stringify(metadata,null,2));
  }
} finally {socket.close();}
