import {fontRequests} from './font-delivery.js';
import {hostFonts} from './host-fonts.js';
// Font-file selections publish only after loading; glyphs are synchronous WASM work.
export function dynamicFonts(probe, request = globalThis.fetch, density = () => globalThis.devicePixelRatio ?? 1,
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
  // Keep selections serialized through publication.
  request = fontRequests(request);
  const identity = json => {
    const values = JSON.parse(json);
    const selected = {uiFontName:values.uiFontName ?? '',fontName:values.fontName ?? 'monospace',
      uiFontSize:Math.max(0,Math.min(48,values.uiFontSize ?? 14)),
      fontSize:Math.max(8,Math.min(28,values.fontSize ?? 14))};
    return JSON.stringify(selected);
  };
  const installed = new Map(), failures = new Map();
  let wanted, running = false, retry = false, rejected = null, current = seed ? identity(seed.selection) : null;
  let disposed = false;
  globalThis.addEventListener?.('pagehide', event => {
    if (!event.persisted) { disposed = true; wanted = null; installed.clear(); failures.clear(); }
  });
  if (seed) installed.set(current, seed.chains);
  const restore = selected => {
    rejected = selected;
    if (current) probe.WorkspaceFontRestore?.(current);
    report('Could not load selected font. Previous font settings retained.', () => {
      failures.delete(selected); wanted = selected; retry = true;
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
          if (retry) headers['x-slop-font-retry'] = '1';
          retry = false;
          const response = await request('/host/fonts', {method:'POST', headers, body:JSON.stringify({...JSON.parse(selected),protocol:2})});
          if (!response.ok) throw Error('Native font preparation unavailable');
          const result = await response.json();
          if (disposed || selected !== wanted) continue; // Stale work is never marked installed.
          const fonts = result.files ? new Map(await Promise.all(Object.entries(result.files).map(async([name, hash])=>
            [name,await host.file(hash)]))) : new Map(Object.entries(result.fonts).map(([name, bytes]) =>
            [name, Uint8Array.from(atob(bytes), c => c.charCodeAt(0))]));
          if (result.files) await host.warm([...new Set(result.faces.map(face=>JSON.parse(face.bake).family))]);
          if (disposed || selected !== wanted) continue;
          for (const face of result.faces) probe.WorkspaceFont(fonts.get(face.font), face.bake, -1);
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
