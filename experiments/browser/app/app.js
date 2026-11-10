import { dotnet } from './_framework/dotnet.js';
import { createSceneDefaultRenderer, loadSceneShaders } from './graphics/renderer.js';

const status = document.querySelector('#status');
const rows = document.querySelector('#checks tbody');
const check = (name, ok, detail) => {
  const row = rows.insertRow();
  row.insertCell().textContent = name;
  const cell = row.insertCell();
  cell.className = ok ? 'ok' : 'blocked';
  cell.textContent = detail;
};
window.__probeResult = { status: 'running' };
let renderer;
try {
  const runtime = await dotnet.create();
  const { Probe } = await runtime.getAssemblyExports('Probe.dll');
  const manifest = await (await fetch('./game/manifest.json')).json();
  const loaded = [], loadErrors = [];
  for (const item of manifest) {
    try {
      const response = await fetch(`./game/${item.file}`);
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      loaded.push(Probe.LoadAssembly(new Uint8Array(await response.arrayBuffer())));
    } catch (error) { loadErrors.push({ file: item.file, error: String(error) }); }
  }
  const managed = JSON.parse(Probe.Run());
  check('Execution', managed.browser && managed.architecture === 'Wasm', `${managed.runtime} / ${managed.architecture}`);
  for (const item of managed.checks) check(item.name, item.ok, item.ok ? item.value : item.errors.at(-1));

  const geometry = JSON.parse(Probe.SceneGeometry());
  const canvas = document.querySelector('#scene');
  renderer = createSceneDefaultRenderer(canvas, await loadSceneShaders());
  renderer.draw({ vertices: new Float32Array(geometry.vertices), clear: [0.055, 0.075, 0.095, 1] });
  const pixel = renderer.readPixel(canvas.width / 2, canvas.height / 2);
  const graphics = { backend: renderer.backend, vertexCount: geometry.vertices.length / 14, centerPixel: pixel, source: geometry.source };
  check('Local graphics', true, `${graphics.backend}; ${graphics.vertexCount} vertices from WASM`);
  document.querySelector('#scene-caption').textContent = `${geometry.source ?? 'Game coordinate probe'} → WebGL2. Diagnostic geometry only; no colony has loaded.`;

  // Health is proxied on the host. Neither the daemon token nor its raw socket is
  // exposed to JavaScript; game and graphics execution stay in this renderer.
  const response = await fetch('/host/health');
  const bytes = new Uint8Array(await response.arrayBuffer());
  const host = { ok: response.ok && bytes.length >= 2 && bytes[0] === 8 && bytes[1] === 1,
    status: response.status, bytes: bytes.length };
  check('Host-side slopd', host.ok, host.ok ? 'Live protobuf health response' : `Health unavailable (HTTP ${host.status})`);

  const blocked = managed.checks.filter(item => !item.ok).map(item => item.name);
  window.__probeResult = { status: 'blocked', scope: 'Managed game-code and WebGL proof; colony startup remains blocked',
    managed, graphics, host, blocked, loaded, loadErrors, manifest };
  status.textContent = 'Local WASM + WebGL proof is running. Game startup remains blocked; see exact failures below.';
} catch (error) {
  window.__probeResult = { status: 'fail', error: String(error), stack: error.stack };
  status.textContent = `Prototype failed: ${error}`;
} finally {
  document.querySelector('#result').textContent = JSON.stringify(window.__probeResult, null, 2);
}
window.addEventListener('pagehide', () => renderer?.dispose(), { once: true });
