// Settings/resize events own requests. Drawing never probes the host or bakes.
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
  const identity = json => {
    const values = JSON.parse(json);
    const selected = {uiFontName:values.uiFontName ?? '',fontName:values.fontName ?? 'monospace',
      uiFontSize:Math.max(0,Math.min(48,values.uiFontSize ?? 14)),
      fontSize:Math.max(8,Math.min(28,values.fontSize ?? 14))};
    selected.density = [1,5/3,2].reduce((best, next) => Math.abs(next-density()) < Math.abs(best-density()) ? next : best);
    return JSON.stringify(selected);
  };
  const installed = new Map(), failures = new Map();
  let wanted, running = false, retry = false, rejected = null, current = seed ? identity(seed.selection) : null, lastJson;
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
          probe.WorkspaceFontsReady(); current = selected;
          report(''); break;
        }
        if (failures.has(selected)) { restore(selected); break; }
        try {
          const headers = {'Content-Type':'application/json'};
          if (retry) headers['x-slop-font-retry'] = '1';
          retry = false;
          const response = await request('/host/fonts', {method:'POST', headers, body:selected});
          if (!response.ok) throw Error('Native font preparation unavailable');
          const result = await response.json();
          if (selected !== wanted) continue; // Stale work is never marked installed.
          const allocation = result.faces.reduce((bytes, face) => {
            const atlas = JSON.parse(face.bake).atlas;
            return bytes + atlas.width * atlas.height * 4;
          }, 0);
          if (probe.WorkspaceFontCapacity && !probe.WorkspaceFontCapacity(allocation))
            throw Error('Native font atlas memory limit reached');
          const fonts = new Map(Object.entries(result.fonts).map(([name, bytes]) =>
            [name, Uint8Array.from(atob(bytes), c => c.charCodeAt(0))]));
          for (const face of result.faces) probe.WorkspaceFont(fonts.get(face.font), face.bake, -1);
          const chains = JSON.stringify(result.chains);
          probe.WorkspaceFontChains(chains);
          probe.WorkspaceFontsReady();
          installed.set(selected, chains); current = selected; rejected = null;
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
    lastJson = json;
    const next = identity(json);
    if (wanted === next) return Promise.resolve();
    if (rejected && next === current) { wanted = next; return Promise.resolve(); }
    if (!wanted && current === next) { wanted = next; return Promise.resolve(); }
    wanted = next;
    return load();
  };
  globalThis.addEventListener?.('resize', () => { if (lastJson) change(lastJson); });
  return change;
}

// Native dirty events arrive immediately; coalesce slider and menu bursts
// independently of profile-save timing. No timer is scheduled by drawing.
export function debounceFonts(change, delay = 200) {
  let timer;
  return json => { clearTimeout(timer); timer = setTimeout(() => void change(json), delay); };
}
