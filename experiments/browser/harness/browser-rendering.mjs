// Rendering mode is an explicit experiment input, never a product setting.
import {readFile} from 'node:fs/promises';

export function renderingFlags(mode='auto') {
  if(mode==='auto')return [];
  if(mode==='swiftshader')return ['--use-angle=swiftshader','--enable-unsafe-swiftshader'];
  // Select ANGLE's GL backend without overriding Chrome's driver blocklist.
  // Hardware availability still comes from the reported device/feature status.
  if(mode==='angle-gl')return ['--use-angle=gl'];
  throw new Error('PROBE_RENDERING must be auto, swiftshader or angle-gl');
}

export async function browserGpuInfo(profile) {
  const [port,endpoint]=(await readFile(`${profile}/DevToolsActivePort`,'utf8')).trim().split('\n');
  const socket=new WebSocket(`ws://127.0.0.1:${port}${endpoint}`);
  let timer;
  try {
    return await new Promise((resolve,reject)=>{
      timer=setTimeout(()=>reject(new Error('GPU information request timed out')),5000);
      socket.onopen=()=>socket.send(JSON.stringify({id:1,method:'SystemInfo.getInfo'}));
      socket.onerror=()=>reject(new Error('Browser GPU information connection failed'));
      socket.onclose=()=>reject(new Error('Browser closed before GPU information arrived'));
      socket.onmessage=({data})=>{
        const message=JSON.parse(data);
        if(message.id!==1)return;
        if(message.error)reject(new Error(message.error.message));
        else resolve(message.result.gpu);
      };
    });
  } finally {clearTimeout(timer);socket.close();}
}
