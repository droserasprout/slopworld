// Own a headless Firefox via direct WebDriver HTTP for the Canvas primitive check.
import {spawn} from 'node:child_process';
import {readFile,writeFile,mkdir,mkdtemp,copyFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {serve} from './server.mjs';
import path from 'node:path';
import {setTimeout as pause} from 'node:timers/promises';
import {cpuQuota} from './browser-process-cpu.mjs';
const output=process.argv[2];
const solid=process.argv[3]==='solid';
if(!output)throw Error('Expected output report path');
let session,base,log='',directory,server;
const driver=spawn('geckodriver',['--host','127.0.0.1','--port','0'],{
  detached:true,stdio:['ignore','pipe','pipe'],env:{...process.env,DBUS_SESSION_BUS_ADDRESS:'unix:path=/dev/null'},
});
driver.stdout.on('data',x=>{log=(log+x).slice(-8000);});
driver.stderr.on('data',x=>{log=(log+x).slice(-8000);});
async function request(method,route,body) {
  const r=await fetch(base+route,{method,headers:{'Content-Type':'application/json'},
    ...(body===undefined?{}:{body:JSON.stringify(body)}),signal:AbortSignal.timeout(180000)});
  const {value}=await r.json();if(!r.ok||value?.error)throw Error(JSON.stringify(value));return value;
}
try {
  for(let i=0;i<150;i++) {
    const m=/Listening on (127\.0\.0\.1:\d+)/.exec(log);
    if(m){base='http://'+m[1];break;}await pause(100);
  }
  if(!base)throw Error('Geckodriver failed to start');
  const launched=await request('POST','/session',{capabilities:{alwaysMatch:{browserName:'firefox',
    'moz:firefoxOptions':{binary:'/usr/bin/firefox',args:['-headless']}}}});
  session=launched.sessionId;
  const quota=await cpuQuota(launched.capabilities['moz:processID']);
  if(!quota.available||quota.cores===null||quota.cores>2)throw Error('Missing two-core bound');
  if(!launched.capabilities['moz:headless'])throw Error('Firefox must be headless');
  await request('POST',`/session/${session}/timeouts`,{script:180000});
  let script;
  if(solid) {
    directory=await mkdtemp(path.join(tmpdir(),'slop-solid-'));
    for(const file of ['gui.js','gui-pixels.js','gui-tint.js','gui-quads.js'])
      await copyFile(new URL('../runtime/wwwroot/'+file,import.meta.url),path.join(directory,file));
    await copyFile(new URL('./solid-texture-check.js',import.meta.url),path.join(directory,'solid-texture-check.js'));
    await writeFile(path.join(directory,'index.html'),'<!doctype html><script type="module" src="./solid-texture-check.js"></script>');
    const serving=await serve(directory);server=serving.server;
    await request('POST',`/session/${session}/url`,{url:serving.url});
    script='async function bitmapPaintCheck(){const end=performance.now()+30000;while(!window.__probeResult){if(performance.now()>end)throw Error("Pixel check timed out");await new Promise(r=>setTimeout(r,20));}return window.__probeResult;}';
  } else script=await readFile(new URL('./bitmap-paint-check.js',import.meta.url),'utf8');
  const result=await request('POST',`/session/${session}/execute/async`,{
    script:script+'\nconst done=arguments[arguments.length-1];bitmapPaintCheck().then(value=>done({value}),error=>done({error:String(error)}));',args:[],
  });
  if(result.error)throw Error(result.error);
  if(solid && result.value.status!=='ok')throw Error(JSON.stringify(result.value));
  await mkdir(path.dirname(output),{recursive:true});
  await writeFile(output,JSON.stringify({headless:true,cpuQuota:quota,...result.value},null,2)+'\n');
  console.log(JSON.stringify(solid?result.value:result.value.results.map(({samples,...row})=>row),null,2));
} finally {
  if(session)await request('DELETE',`/session/${session}`).catch(()=>{});
  try{process.kill(-driver.pid,'SIGTERM');}catch{}
  if(server)await new Promise(resolve=>server.close(resolve));
  if(directory)await rm(directory,{recursive:true,force:true});
}
