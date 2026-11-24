import {hostFonts} from './host-fonts.js';
import {warmWorkspaceFonts} from './font-delivery.js';
import {installWorkspaceFont} from './workspace-rasterizer.js';
async function installFaces(probe, host, selection, current = () => true) {
  // Fetch each distinct hash once before publishing any selected face.
  const hashes = [...new Set(Object.values(selection.files))];
  const files = new Map(await Promise.all(hashes.map(async hash => [hash, await host.file(hash)])));
  for (const {font, bake, index = -1} of selection.faces) {
    if (!current()) return;
    const bytes = files.get(selection.files[font]);
    if (!bytes) throw Error('Missing font file for ' + font);
    await installWorkspaceFont(probe, bytes, bake, index, current);
  }
}

function families(selection) {
  return [...new Set(selection.faces.map(face => JSON.parse(face.bake).family))];
}

export async function installStartupFonts(probe, request) {
  const catalog = await request('./assets/fonts/catalog.json');
  if (catalog.ok) probe.WorkspaceFontCatalog(await catalog.text());
  const response = await request('./assets/fonts/manifest.json');
  if (!response.ok) throw Error('Host font manifest is missing');
  const selection = await response.json();
  const chains = JSON.stringify(selection.chains);
  probe.WorkspaceFontChains(chains);
  await installFaces(probe, hostFonts(probe, request), selection);
  await warmWorkspaceFonts(probe, families(selection));
  return chains;
}

// Font-file selections publish only after both rasterizers have loaded the bytes.
export function dynamicFonts(probe, request = globalThis.fetch,
  report = (message, retry) => {
    if (message) console.warn(message);
    if (!globalThis.document) return;
    let status = document.getElementById('font-status');
    if (!message) { status?.remove(); return; }
    if (!status) {
      status = document.createElement('p'); status.id = 'font-status'; status.setAttribute('role','status');
      status.style.cssText = 'position:fixed;bottom:0;left:0;z-index:10;background:#222;color:white;padding:8px';
      document.body.append(status);
    }
    status.textContent = message + (retry ? ' Click to retry.' : '');
    status.onclick = retry ?? null;
  }, seed = null) {
  const host=hostFonts(probe,request);
  const identity = json => {
    const values = JSON.parse(json);
    const selected = {uiFontName:values.uiFontName ?? '',fontName:values.fontName ?? 'monospace',
      uiFontSize:Math.max(0,Math.min(48,values.uiFontSize ?? 14)),
      fontSize:Math.max(8,Math.min(28,values.fontSize ?? 14))};
    return JSON.stringify(selected);
  };
  const installed = new Map(), failures = new Map();
  let wanted, running = false, rejected = null, current = seed ? identity(seed.selection) : null;
  let disposed = false;
  globalThis.addEventListener?.('pagehide', event => {
    if (!event.persisted) { disposed = true; wanted = null; installed.clear(); failures.clear(); }
  });
  if (seed) installed.set(current, seed.chains);
  const restore = selected => {
    rejected = selected;
    if (current) probe.WorkspaceFontRestore?.(current);
    report('Could not load selected font. Previous font settings retained.', () => {
      failures.delete(selected); wanted = selected;
      void load();
    });
  };
  const load = async () => {
    if (running) return;
    running = true;
    try {
      while (wanted) {
        const selected = wanted;
        if (installed.has(selected)) {
          probe.WorkspaceFontChains(installed.get(selected));
          probe.WorkspaceFontRestore?.(selected);
          probe.WorkspaceFontsReady(); current = selected;
          report(''); break;
        }
        if (failures.has(selected)) { restore(selected); break; }
        try {
          const headers = {'Content-Type':'application/json'};
          const response = await request('./host/fonts', {method:'POST', headers, body:selected});
          if (!response.ok) throw Error('Native font preparation unavailable');
          const result = await response.json();
          if (disposed || selected !== wanted) continue; // Stale work is never marked installed.
          await host.warm(families(result));
          const currentSelection = () => !disposed && selected === wanted;
          if (!currentSelection()) continue;
          await installFaces(probe, host, result, currentSelection);
          if (!currentSelection()) continue;
          const chains = JSON.stringify(result.chains);
          probe.WorkspaceFontChains(chains);
          probe.WorkspaceFontsReady();
          installed.set(selected, chains); current = selected; rejected = null;
          if (installed.size > 128) installed.delete(installed.keys().next().value);
          probe.WorkspaceFontRestore?.(selected); report('');
          if (wanted === selected) break;
        } catch {
          // Failure cache is bounded too. A reload explicitly retries a failed
          // host preparation; unrelated native writes cannot start a retry storm.
          failures.set(selected, true);
          if (failures.size > 8) failures.delete(failures.keys().next().value);
          if (wanted === selected) { restore(selected); break; }
        }
      }
    } finally { running = false; }
  };
  const change = json => {
    if (disposed) return Promise.resolve();
    const next = identity(json);
    if (wanted === next) return Promise.resolve();
    if (rejected && next === current) { wanted = next; return Promise.resolve(); }
    if (!wanted && current === next) { wanted = next; return Promise.resolve(); }
    wanted = next;
    if (current && current !== next) probe.WorkspaceFontHold?.(current);
    return load();
  };
  return change;
}
