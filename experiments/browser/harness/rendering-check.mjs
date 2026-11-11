// Short empty-page backend probe; no WASM/game startup or retained browser.
import {mkdtemp,writeFile,rm,readFile,cp,copyFile} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {fileURLToPath} from 'node:url';
const root=await mkdtemp(`${tmpdir()}/slopweb-rendering-`);
const reuse=process.argv[2]==='reuse';
const output=fileURLToPath(new URL(`../artifacts/${reuse?'canvas-reuse':'rendering'}-check.json`,import.meta.url));
try {
  if(reuse) {
    await cp(fileURLToPath(new URL('../runtime/wwwroot',import.meta.url)),root,{recursive:true});
    await copyFile(fileURLToPath(new URL('./canvas-reuse-check.js',import.meta.url)),`${root}/canvas-reuse-check.js`);
  }
  await writeFile(`${root}/index.html`,reuse?'<!doctype html><script type="module" src="./canvas-reuse-check.js"></script>':'<!doctype html><script>globalThis.__probeResult={status:"ok"}</script>');
  await promisify(execFile)(process.execPath,[fileURLToPath(new URL('./validate.mjs',import.meta.url)),root,'/',output],{
    env:{...process.env,PROBE_RENDERING:'auto',PROBE_KEEP_OPEN:'0',PROBE_TIMEOUT_MS:'10000'},maxBuffer:1024*1024,
  });
  const report=JSON.parse(await readFile(output,'utf8'));
  if(!report.completed||report.result?.status!=='ok'||report.gpu.error||!report.gpu.devices?.length)throw new Error(JSON.stringify({error:'Rendering check failed',result:report.result}));
  if(reuse)console.log(JSON.stringify(report.result));
  console.log(JSON.stringify({renderingMode:report.renderingMode,devices:report.gpu.devices,features:report.gpu.featureStatus}));
} finally {await rm(root,{recursive:true,force:true});}
