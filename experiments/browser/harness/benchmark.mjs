// Reproducible terminal workloads through the real host bridge and WASM mod.
// This runner owns fixture lifetime; reports never include host credentials.
import {readFile, writeFile, mkdir} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {processSampler} from './browser-process-cpu.mjs';
import {browserGpuInfo} from './browser-rendering.mjs';
import {jiterpreterSnapshot} from './browser-jiterpreter.mjs';

const [profile, output, phaseList='idle,output,typing,keystrokes,scrolling,switching',quadCache='on',canvasReuse='keep',packetCache='keep',idleYield='keep',discardedDrawing='keep',earlyInput='keep',comparison='none'] = process.argv.slice(2);
if(!['keep','on','off'].includes(earlyInput))throw new Error('Early input flush must be keep, on or off');
if(!['keep','on','off'].includes(discardedDrawing))throw new Error('Discarded drawing suppression must be keep, on or off');
if(!['keep','on','off'].includes(idleYield))throw new Error('Idle yield must be keep, on or off');
if(!['keep','on','off'].includes(packetCache))throw new Error('Packet cache must be keep, on or off');
if(!['keep','on','off'].includes(canvasReuse))throw new Error('Canvas reuse must be keep, on or off');
if(!['on','off'].includes(quadCache))throw new Error('Quad cache must be on or off');
const phases=phaseList.split(',');
const stageProfile=process.env.PROBE_STAGE_PROFILE!=='0';
if(!stageProfile && phases.includes('cyrillic'))throw Error('Cyrillic coverage checks require stage profiling');
const comparisonExport={glyph:'WorkspaceGlyphCache',input:'WorkspaceInputFlushBeforeRepaint',cell:'WorkspaceCellFastPath',tint:'configureGuiTintCache',command:'WorkspaceCommandValueCache',clip:'configureGuiScreenClipFastPath'}[comparison];
if(comparison!=='none' && (!comparisonExport || phases.length!==4 || new Set(phases).size!==1))
  throw new Error('Comparison must be glyph, input, cell, tint, command or clip with four identical workload phases');
const comparisonCall=['tint','clip'].includes(comparison) ? `(async enabled => (await import("/gui.js")).${comparisonExport}(enabled))` : `window.__managedProbe.${comparisonExport}`;
if (phases.some(phase=>!['idle','output','typing','cyrillic','keystrokes','scrolling','switching'].includes(phase)))
  throw new Error('Unknown benchmark phase');
if (!profile || !output) throw new Error('Usage: benchmark.mjs PROFILE OUTPUT');
const port = (await readFile(path.join(profile, 'DevToolsActivePort'), 'utf8')).split('\n')[0];
const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
const page = targets.find(target => target.type === 'page' && target.url.startsWith('http://127.0.0.1:'));
if (!page) throw new Error('No local workspace page');
const socket = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((resolve, reject) => {socket.onopen=resolve; socket.onerror=reject;});
let serial=0;
const pending=new Map();
const call=(method, params={}) => new Promise((resolve,reject) => {
  const id=++serial;
  const timer=setTimeout(() => {pending.delete(id);reject(new Error('Timeout: '+method));},120000);
  pending.set(id,{resolve,reject,timer});socket.send(JSON.stringify({id,method,params}));
});
socket.onmessage=({data}) => {
  const message=JSON.parse(data), entry=pending.get(message.id);
  if (!entry) return;
  pending.delete(message.id);clearTimeout(entry.timer);
  if (message.error) entry.reject(new Error(JSON.stringify(message.error)));
  else entry.resolve(message.result);
};
const evaluate=async expression => {
  const result=await call('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true});
  if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description ?? JSON.stringify(result.exceptionDetails));
  return result.result.value;
};
const distribution = values => {
  const sorted=values.toSorted((a,b)=>a-b);
  const at=p=>sorted.length ? sorted[Math.floor(p*(sorted.length-1))] : null;
  return {count:sorted.length,p50:at(.5),p95:at(.95),p99:at(.99),max:sorted.at(-1)??null};
};
const metrics = result => Object.fromEntries(result.metrics.map(({name,value})=>[name,value]));
const report={version:1, phases:[], cleanup:false};
const processCpu=processSampler(profile);
let installed=false;
let previousQuadCache;
let previousCanvasReuse;
let previousPacketCache;
let previousIdleYield;
let previousDiscardedDrawing;
let previousEarlyInput;
let previousComparison;
let previousTintProfile;
await mkdir(path.dirname(output),{recursive:true});
try {
  report.browser=await call('Browser.getVersion');
  report.gpu=await browserGpuInfo(profile);
  // A retained browser may still be bootstrapping after an adapter refresh.
  // Wait on this runtime; never create fixtures against an unready workspace.
  await evaluate(`(async () => {
    const deadline=performance.now()+110000;
    while (window.__workspace?.status !== 'running') {
      if (window.__workspace?.status === 'fail') throw new Error(window.__workspace.error);
      if (performance.now()>deadline) throw new Error('Workspace startup is still pending');
      await new Promise(resolve=>setTimeout(resolve,250));
    }
  })()`);
  report.configuration=await evaluate(`(() => {
    const canvas=document.querySelector('canvas');
    return {viewport:[innerWidth,innerHeight],canvas:[canvas.width,canvas.height],dpr:devicePixelRatio,
      canvasContext:canvas.getContext('2d').getContextAttributes(),
      runtime:window.__workspace.status, url:location.pathname+location.search};
  })()`);
  previousQuadCache=await evaluate(`window.__managedProbe.WorkspaceQuadCache(${quadCache==='on'})`);
  report.configuration.quadCache=quadCache;
  report.configuration.cpuQuota=await processCpu.quota();
  report.configuration.canvasReuse=canvasReuse;
  report.configuration.packetCache=packetCache;
  report.configuration.idleYield=idleYield;
  report.configuration.earlyInput=earlyInput;
  if(earlyInput!=='keep')previousEarlyInput=await evaluate(`window.__managedProbe.WorkspaceInputFlushBeforeRepaint(${earlyInput==='on'})`);
  report.configuration.discardedDrawing=discardedDrawing;
  if(discardedDrawing!=='keep')previousDiscardedDrawing=await evaluate(`window.__managedProbe.WorkspaceDiscardedDrawing(${discardedDrawing==='on'})`);
  if(idleYield!=='keep')previousIdleYield=await evaluate(`window.__workspace.setIdleYield(${idleYield==='on'})`);
  if(packetCache!=='keep')previousPacketCache=await evaluate(`window.__managedProbe.WorkspacePacketCache(${packetCache==='on'})`);
  if(canvasReuse!=='keep')previousCanvasReuse=await evaluate(`window.__workspace.setCanvasReuse(${canvasReuse==='on'})`);
  const script=await readFile(fileURLToPath(new URL('./benchmark-workload.js',import.meta.url)),'utf8');
  await evaluate(script);installed=true;
  const fixturePath=fileURLToPath(new URL('./benchmark-fixture.py',import.meta.url));
  const command="python3 -u '"+fixturePath.replaceAll("'", "'\\''")+"'";
  report.fixture=await evaluate(`window.__benchmark.prepare(${JSON.stringify(command)})`);
  report.configuration.comparison=comparison;
  report.configuration.stageProfile=stageProfile;
  if(comparisonExport)previousComparison=await evaluate(`${comparisonCall}(true)`);
  if(comparison==='tint')previousTintProfile=await evaluate('(async()=> (await import("/gui.js")).configureGuiTintProfile(true))()');
  console.log('Benchmark fixtures ready');
  await call('Performance.enable');
  for (const [index,phase] of phases.entries()) {
    const comparisonEnabled=comparisonExport ? [false,true,true,false][index] : null;
    if(comparisonExport)await evaluate(`${comparisonCall}(${comparisonEnabled})`);
    // Repeated phases must start with the same screen instead of gradually
    // replacing the 200-line fixture with shorter echo acknowledgments.
    if(comparisonExport || phase==='keystrokes')await evaluate('window.__benchmark.reset()');
    if(comparison==='tint')await evaluate('(async()=> (await import("/gui.js")).configureGuiTintProfile(true))()');
    const jiterpreterBefore=await evaluate(`(${jiterpreterSnapshot.toString()})()`);
    const processBefore=await processCpu.sample();
    const before=metrics(await call('Performance.getMetrics'));
    const result=await evaluate(`window.__benchmark.run(${JSON.stringify(phase)},{stageProfile:${stageProfile}})`);
    const after=metrics(await call('Performance.getMetrics'));
    const processAfter=await processCpu.sample();
    const jiterpreterAfter=await evaluate(`(${jiterpreterSnapshot.toString()})()`);
    const stageMeans={};
    for (const key of Object.keys(result.stages[0]??{}))
      stageMeans[key]=result.stages.reduce((sum,stage)=>sum+stage[key],0)/result.stages.length;
    // Cache hits can dominate cadence. Keep actual target repaints separate so
    // a higher hit rate cannot masquerade as cheaper changed-content rendering.
    const targetRepaints=result.stages.filter(stage=>stage.targetWrites>0);
    const targetRepaintMs=Object.fromEntries(
      ['managedMs','repaintMs','serializationMs','canvasMs'].map(key=>
        [key,distribution(targetRepaints.map(stage=>stage[key]).filter(Number.isFinite))]));
    const summary={phase,comparisonEnabled,durationMs:result.durationMs,frameMs:distribution(result.intervals),
      drawFrameMs:distribution(result.drawIntervals),
      targetRepaintMs,
      latencyMs:distribution(result.latencyMs),
      wireLatencyMs:result.wireTimings?.length ? Object.fromEntries(
        ['dispatchToSendMs','sendToMessageMs','messageToObservedMs'].map(key=>
          [key,distribution(result.wireTimings.map(row=>row[key]).filter(value=>value!==null))])) : null,
      latencyDefinition:phase==='keystrokes'?'Single DOM key dispatch to byte-verified echo in native receive buffer'
        :['typing','cyrillic'].includes(phase)?'DOM command dispatch to matching reply in native receive buffer'
        :phase==='switching'?'Native Open to selected pane draw submission':null,
      rendererTaskCpuPercent:100*(after.TaskDuration-before.TaskDuration)/(after.Timestamp-before.Timestamp),
      browserProcesses:processCpu.summarize(processBefore,processAfter),
      jsHeapUsed:after.JSHeapUsedSize,jsHeapTotal:after.JSHeapTotalSize,stageMeans};
    if(comparison==='tint')summary.tint=await evaluate('(async()=> (await import("/gui.js")).guiTintProfile())()');
    report.phases.push({summary, result, before, after,jiterpreterBefore,jiterpreterAfter});
    console.log(JSON.stringify(summary));
    if (process.env.PROBE_SCREENSHOTS !== '0') {
      const screenshot=await call('Page.captureScreenshot',{format:'png'});
      await writeFile(output+'.'+phase+(comparisonExport?'.'+index:'')+'.png',Buffer.from(screenshot.data,'base64'));
    }
  }
} catch(error) {
  report.error=String(error);process.exitCode=1;
} finally {
  if (installed) {
    try {await evaluate('window.__benchmark.cleanup()');report.cleanup=true;}
    catch(error) {report.cleanupError=String(error);process.exitCode=1;}
  }
  if(previousTintProfile!==undefined)await evaluate(`(async()=> (await import("/gui.js")).configureGuiTintProfile(${previousTintProfile}))()`).catch(error=>{report.tintProfileRestoreError=String(error);process.exitCode=1;});
  if(previousQuadCache!==undefined)
    await evaluate(`window.__managedProbe.WorkspaceQuadCache(${previousQuadCache})`).catch(error=>{report.cacheRestoreError=String(error);process.exitCode=1;});
  if(previousCanvasReuse!==undefined)
    await evaluate(`window.__workspace.setCanvasReuse(${previousCanvasReuse})`).catch(error=>{report.canvasRestoreError=String(error);process.exitCode=1;});
  if(previousPacketCache!==undefined)
    await evaluate(`window.__managedProbe.WorkspacePacketCache(${previousPacketCache})`).catch(error=>{report.packetRestoreError=String(error);process.exitCode=1;});
  if(previousIdleYield!==undefined)
    await evaluate(`window.__workspace.setIdleYield(${previousIdleYield})`).catch(error=>{report.idleRestoreError=String(error);process.exitCode=1;});
  if(previousComparison!==undefined)
    await evaluate(`${comparisonCall}(${previousComparison})`).catch(error=>{report.comparisonRestoreError=String(error);process.exitCode=1;});
  if(previousEarlyInput!==undefined)
    await evaluate(`window.__managedProbe.WorkspaceInputFlushBeforeRepaint(${previousEarlyInput})`).catch(error=>{report.inputRestoreError=String(error);process.exitCode=1;});
  if(previousDiscardedDrawing!==undefined)
    await evaluate(`window.__managedProbe.WorkspaceDiscardedDrawing(${previousDiscardedDrawing})`).catch(error=>{report.drawingRestoreError=String(error);process.exitCode=1;});
  await mkdir(path.dirname(output),{recursive:true});
  await writeFile(output,JSON.stringify(report,null,2)+'\n');
  console.log(JSON.stringify({output,error:report.error,cleanup:report.cleanup,cleanupError:report.cleanupError}));
  for (const entry of pending.values()) clearTimeout(entry.timer);
  socket.close();
}
