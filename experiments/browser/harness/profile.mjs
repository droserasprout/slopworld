// Read-only baseline capture. No input is sent to the selected host session.
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

const [profile, output, seconds = '15'] = process.argv.slice(2);
if (!profile || !output || !Number.isFinite(Number(seconds)) || Number(seconds) <= 0)
  throw new Error('Usage: profile.mjs PROFILE OUTPUT_SECONDS_JSON [SECONDS]');
const port = (await readFile(path.join(profile, 'DevToolsActivePort'), 'utf8')).split('\n')[0];
const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
const page = targets.find(t => t.type === 'page' && t.url.startsWith('http://127.0.0.1:'));
if (!page) throw new Error('No live local workspace');
const socket = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
let serial = 0;
const pending = new Map();
const call = (method, params = {}) => new Promise((resolve, reject) => {
  const id = ++serial;
  const timer = setTimeout(() => { pending.delete(id); reject(new Error(`Timed out: ${method}`)); }, 60000);
  pending.set(id, {resolve, reject, timer});
  socket.send(JSON.stringify({id, method, params}));
});
socket.onmessage = ({data}) => {
  const message = JSON.parse(data), entry = pending.get(message.id);
  if (!entry) return;
  pending.delete(message.id); clearTimeout(entry.timer);
  if (message.error) entry.reject(new Error(JSON.stringify(message.error)));
  else entry.resolve(message.result);
};
const evaluate = async expression => {
  const result = await call('Runtime.evaluate', {expression, returnByValue:true});
  if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails));
  return result.result.value;
};
try {
  const configuration = await call('Browser.getVersion');
  const workspace = await evaluate(`(() => {
    if (window.__workspace?.status !== 'running') throw new Error('Workspace is not running');
    const canvas = document.querySelector('canvas');
    return { state: window.__workspace, viewport: [innerWidth,innerHeight],
      canvas: [canvas.width,canvas.height], dpi: devicePixelRatio, userAgent:navigator.userAgent };
  })()`);
  await call('Performance.enable');
  await call('Profiler.enable');
  await call('Profiler.setSamplingInterval', {interval:1000});
  const before = await call('Performance.getMetrics');
  await evaluate(`(() => {
    if (window.__workspaceProfile) window.__workspaceProfile.length = 0;
    const sample = window.__baselineProfile = { intervals: [], started: performance.now(), last: null, running: true };
    function frame(now) {
      if (!sample.running) return;
      if (sample.last !== null) sample.intervals.push(now - sample.last);
      sample.last = now; requestAnimationFrame(frame);
    }
    requestAnimationFrame(frame);
  })()`);
  await call('Profiler.start');
  await delay(Number(seconds) * 1000);
  const cpu = await call('Profiler.stop');
  const after = await call('Performance.getMetrics');
  const frames = await evaluate(`(() => {
    const sample = window.__baselineProfile; sample.running = false;
    return { intervals: sample.intervals, duration: performance.now() - sample.started,
      stages: window.__workspaceProfile ?? null,
      workspace: window.__workspace };
  })()`);
  const sorted = frames.intervals.toSorted((a,b) => a-b);
  const percentile = p => sorted.length ? sorted[Math.min(sorted.length-1,Math.floor(p*(sorted.length-1)))] : null;
  const metrics = Object.fromEntries(after.metrics.map(({name,value}) => [name,value]));
  const start = Object.fromEntries(before.metrics.map(({name,value}) => [name,value]));
  const summary = { workload:'observe-current-session', seconds:Number(seconds), frames:sorted.length,
    frameMs:{p50:percentile(.5),p95:percentile(.95),p99:percentile(.99),max:sorted.at(-1)},
    rendererTaskCpuPercent:100*(metrics.TaskDuration-start.TaskDuration)/(metrics.Timestamp-start.Timestamp),
    jsHeapUsed:metrics.JSHeapUsedSize, jsHeapTotal:metrics.JSHeapTotalSize };
  if (frames.stages?.length) {
    summary.stageMeans = Object.fromEntries(Object.keys(frames.stages[0]).map(key =>
      [key, frames.stages.reduce((sum, frame) => sum + frame[key], 0) / frames.stages.length]));
    summary.stageFrames = frames.stages.length;
  }
  const nodes = new Map(cpu.profile.nodes.map(node => [node.id, node.callFrame]));
  const selfTime = new Map();
  for (let i = 0; i < (cpu.profile.samples?.length ?? 0); i++) {
    const node = nodes.get(cpu.profile.samples[i]);
    const label = `${node.functionName || '(anonymous)'} ${node.url}:${node.lineNumber + 1}`;
    selfTime.set(label, (selfTime.get(label) ?? 0) + cpu.profile.timeDeltas[i]);
  }
  summary.topSelfTimeMs = [...selfTime].sort((a,b) => b[1]-a[1]).slice(0,20)
    .map(([frame,microseconds]) => ({frame,ms:microseconds/1000}));
  await mkdir(path.dirname(output), {recursive:true});
  await writeFile(output, JSON.stringify({summary,configuration,workspace,before,after,frames},null,2)+'\n');
  await writeFile(output+'.cpuprofile', JSON.stringify(cpu.profile));
  console.log(JSON.stringify({output,...summary},null,2));
} finally {
  for (const entry of pending.values()) clearTimeout(entry.timer);
  socket.close();
}
