// Execute local probes in a real Chromium renderer through CDP, without npm packages.
import { spawn } from 'node:child_process';
import { mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
import { serve } from './server.mjs';
import {renderingFlags,browserGpuInfo} from './browser-rendering.mjs';
import {processSampler} from './browser-process-cpu.mjs';

const [directory, page = '/', output = 'experiments/browser/artifacts/browser.json'] = process.argv.slice(2);
if (!directory) throw new Error('Usage: validate.mjs BUILD_DIRECTORY [PAGE] [REPORT]');
const renderingMode=process.env.PROBE_RENDERING??'auto';
const rendering=renderingFlags(renderingMode);
const cpuLimit=process.env.PROBE_CPU_LIMIT_CORES===undefined?null:Number(process.env.PROBE_CPU_LIMIT_CORES);
if(cpuLimit!==null&&(!Number.isFinite(cpuLimit)||cpuLimit<=0))throw new Error('Invalid PROBE_CPU_LIMIT_CORES');
const profile = await mkdtemp(path.join(os.tmpdir(), 'slopworld-wasm-'));
const { server, url } = await serve(directory);
const events = [];
let socket, chrome;
try {
  chrome = spawn(process.env.CHROMIUM ?? 'chromium', [
    '--headless=new', '--remote-debugging-port=0', `--user-data-dir=${profile}`,
    '--no-first-run', '--no-default-browser-check', '--disable-dev-shm-usage',
    ...rendering,
    ...(process.env.PROBE_WEBGPU === '1' ? ['--enable-unsafe-webgpu'] : []), 'about:blank',
  ], { stdio: ['ignore', 'ignore', 'pipe'],
    // Chromium can ask the desktop bus to migrate it into an uncapped app scope.
    // Bounded diagnostics need no desktop integration; retain the launcher cgroup.
    env:cpuLimit===null?process.env:{...process.env,DBUS_SESSION_BUS_ADDRESS:'unix:path=/dev/null'},
  });
  let stderr = '';
  chrome.stderr.on('data', data => { stderr = (stderr + data).slice(-16000); });
  chrome.on('error', error => { stderr += String(error); });
  let port;
  for (let i = 0; i < 150; i++) {
    try { port = (await readFile(path.join(profile, 'DevToolsActivePort'), 'utf8')).split('\n')[0]; break; }
    catch { await delay(100); }
  }
  if (!port) throw new Error(`Chromium did not start: ${stderr}`);
  const cpuQuota=await processSampler(profile).quota();
  if(cpuLimit!==null&&(!cpuQuota.available||cpuQuota.cores===null||cpuQuota.cores>cpuLimit))
    throw new Error('Diagnostic browser escaped its required CPU quota: '+JSON.stringify(cpuQuota));
  const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
  socket = new WebSocket(targets.find(target => target.type === 'page').webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
  let serial = 0;
  let loadingStages = 0;
  const pending = new Map();
  socket.onmessage = ({ data }) => {
    const message = JSON.parse(data);
    if (message.id) {
      const entry = pending.get(message.id);
      pending.delete(message.id);
      clearTimeout(entry?.timer);
      if (message.error) entry?.reject(new Error(JSON.stringify(message.error)));
      else entry?.resolve(message.result);
    } else if (['Runtime.consoleAPICalled', 'Runtime.exceptionThrown', 'Log.entryAdded'].includes(message.method)) {
      events.push(message);
      if (process.env.PROBE_PROGRESS === '1' && message.method === 'Runtime.consoleAPICalled') {
        const value = message.params.args?.[0]?.value;
        if (typeof value === 'string' && /^(Entry:|Loading stage:|Browser:)/.test(value)) {
          // Retained runs may reload repeatedly while their parent is not reading
          // stdout. Keep constructor progress bounded so logs cannot stall CDP.
          if (!value.startsWith('Loading stage:') || ++loadingStages % 100 === 1)
            console.log(value);
        }
      }
    }
  };
  const call = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++serial;
    const timer = setTimeout(() => {
      pending.delete(id);
      reject(new Error(`Chromium command timed out: ${method}; the renderer may be blocked in synchronous game code`));
    }, Number(process.env.PROBE_COMMAND_TIMEOUT_MS ?? 15000));
    pending.set(id, { resolve, reject, timer });
    socket.send(JSON.stringify({ id, method, params }));
  });
  await call('Runtime.enable');
  await call('Page.enable');
  await call('Log.enable');
  if (process.env.PROBE_WIDTH || process.env.PROBE_HEIGHT || process.env.PROBE_DPR) {
    const width = Number(process.env.PROBE_WIDTH ?? 1280);
    const height = Number(process.env.PROBE_HEIGHT ?? 720);
    const deviceScaleFactor = Number(process.env.PROBE_DPR ?? 1);
    if (!Number.isInteger(width) || !Number.isInteger(height) || width <= 0 || height <= 0 ||
        !Number.isFinite(deviceScaleFactor) || deviceScaleFactor <= 0)
      throw new Error('Invalid PROBE_WIDTH, PROBE_HEIGHT or PROBE_DPR');
    await call('Emulation.setDeviceMetricsOverride', {width,height,deviceScaleFactor,mobile:false});
  }
  await call('Page.navigate', { url: url + page });
  let result;
  const deadline = Date.now() + Number(process.env.PROBE_TIMEOUT_MS ?? 180000);
  while (Date.now() < deadline) {
    const response = await call('Runtime.evaluate', {
      expression: 'globalThis.__probeResult ?? null', returnByValue: true,
    });
    if (response.result.value != null && response.result.value.status !== 'running') {
      result = response.result.value; break;
    }
    await delay(250);
  }
  const dom = await call('Runtime.evaluate', { expression: 'document.body.innerText', returnByValue: true });
  const gpu=await browserGpuInfo(profile).catch(error=>({error:String(error)}));
  const report = { url: url + page, completed: result !== undefined, renderingMode, cpuQuota, gpu, result, body: dom.result.value, events };
  await mkdir(path.dirname(output), { recursive: true });
  await writeFile(output, JSON.stringify(report, null, 2) + '\n');
  if (process.env.PROBE_SAVE_FILE && report.completed && !result?.error) {
    const saved = await call('Runtime.evaluate', {
      expression: 'globalThis.__gameSaveBase64 ?? null', returnByValue: true,
    });
    if (typeof saved.result.value !== 'string') throw new Error('No game save was exported');
    await mkdir(path.dirname(process.env.PROBE_SAVE_FILE), { recursive: true });
    await writeFile(process.env.PROBE_SAVE_FILE, Buffer.from(saved.result.value, 'base64'));
  }
  let clip;
  if (process.env.PROBE_CANVAS_SCREENSHOT === '1') {
    const bounds = await call('Runtime.evaluate', { expression: `(() => {
      const canvas = document.querySelector('canvas');
      if (!canvas) return null;
      const r = canvas.getBoundingClientRect();
      return { x: r.x + scrollX, y: r.y + scrollY, width: r.width, height: r.height, scale: 1 };
    })()`, returnByValue: true });
    clip = bounds.result.value;
    // A failed startup may have no canvas. Preserve its original failure report
    // and capture the page instead of replacing it with a screenshot error.
  }
  if (process.env.PROBE_SCREENSHOTS !== '0') {
    const screenshot = await call('Page.captureScreenshot', { format: 'png', ...(clip ? {clip, captureBeyondViewport: true} : {}) });
    await writeFile(output.replace(/\.json$/, '') + '.png', Buffer.from(screenshot.data, 'base64'));
  }
  console.log(JSON.stringify({ completed: report.completed, status: result?.status,
    error: result?.error, graphics: result?.graphics, host: result?.host,
    checks: (result?.checks ?? result?.managed?.checks)?.map(({ name, ok, pass, value, errors }) =>
      ({ name, ok, pass, value, errors })), report: output }, null, 2));
  if (!report.completed || result?.status === 'fail' || result?.error) process.exitCode = 1;
  if (process.env.PROBE_KEEP_OPEN === '1') {
    console.log(JSON.stringify({ liveProfile: profile, url: url + page, browserPid: chrome.pid }));
    await new Promise(resolve => {
      chrome.once('exit', resolve);
      process.once('SIGINT', resolve);
      process.once('SIGTERM', resolve);
    });
  }
} catch (error) {
  // Preserve the last managed/console progress even if game code prevents CDP
  // evaluation. Never leave an older successful report at the requested path.
  await mkdir(path.dirname(output), { recursive: true });
  await writeFile(output, JSON.stringify({ completed: false, error: String(error), events }, null, 2) + '\n');
  throw error;
} finally {
  socket?.close();
  if (chrome && chrome.exitCode === null) {
    chrome.kill('SIGTERM');
    await Promise.race([new Promise(resolve => chrome.once('exit', resolve)), delay(5000)]);
    if (chrome.exitCode === null) chrome.kill('SIGKILL');
  }
  server.closeAllConnections();
  await new Promise(resolve => server.close(resolve));
  await rm(profile, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
}
