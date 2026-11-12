import {hostFonts} from './host-fonts.js';
// Serialize selection requests through response consumption and publication.
const queues = new WeakMap();
export function fontRequests(fetchFont) {
  return (...args) => {
    const next = (queues.get(fetchFont) ?? Promise.resolve()).then(async () => {
      const response = await fetchFont(...args);
      const result = response.ok ? await response.json() : null;
      return {ok:response.ok, json:async()=>result};
    });
    queues.set(fetchFont, next.catch(()=>{}));
    return next;
  };
}

export function fallbackFonts(probe, wake, fetchFont = globalThis.fetch, report = (message, retry) => {
  let status=document.getElementById('font-fallback-status');
  if (!message) { status?.remove(); return; }
  if (!status) { status=document.createElement('p');status.id='font-fallback-status';status.setAttribute('role','status');
    status.style.cssText='position:fixed;bottom:0;left:0;z-index:10;background:#222;color:white;padding:8px';document.body.append(status); }
  status.textContent=message+' Click to retry.';status.onclick=retry;
}) {
  const fonts=hostFonts(probe,fetchFont);
  let running=false, disposed=false, failed=false;
  async function frame() {
    if (running || disposed || failed) return;
    const pending=probe.WorkspaceFontFallbackTake();
    if (!pending) return;
    const {id,family,scalars}=JSON.parse(pending);
    running=true;
    try {
      const assignments=await fonts.resolve(family,scalars);
      if (disposed) return;
      // Publish synchronously after all decoding: no frame can observe half a reply.
      probe.WorkspaceFontFallbackComplete(JSON.stringify({id,assignments}));
      report(''); wake(); return true;
    } catch {
      if (!disposed) {
        failed=true;
        report('Could not load fallback fonts.',()=>{
          if (disposed) return;
          probe.WorkspaceFontFallbackRetry(); failed=false; report(''); wake();
        });
      }
    } finally {running=false;}
  }
  return {frame,dispose(){disposed=true;fonts.dispose();}};
}

// Drain coverage observed by real startup frames before revealing the workspace.
// There is no authored glyph list and no host rasterization.
export async function warmWorkspaceFonts(probe,families=[]) {
  await hostFonts(probe).warm(families);
  let failure;
  const loader=fallbackFonts(probe,()=>{},globalThis.fetch,(message)=>{if(message)failure=message;});
  let changed=false;
  for (let pass=0;pass<32;pass++) if (!await loader.frame()) break; else changed=true;
  if (failure) {probe.WorkspaceFontFallbackRetry();throw Error(failure);}
  return changed;
}
