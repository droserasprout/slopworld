// Serialize selection and coverage requests through response consumption. The
// host has one bounded font-file preparation worker per workspace server.
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
  const request=fontRequests(fetchFont);
  let running=false, disposed=false, failed=false;
  async function frame() {
    if (running || disposed || failed) return;
    const pending=probe.WorkspaceFontFallbackTake();
    if (!pending) return;
    const {id,family,scalars}=JSON.parse(pending);
    running=true;
    try {
      const response=await request('/host/fonts',{method:'POST',headers:{'Content-Type':'application/json','x-slop-font-retry':'1'},body:JSON.stringify({fallback:{family,scalars}})});
      if (!response.ok) throw Error('Font file delivery failed');
      const result=await response.json();
      const fonts=new Map();
      for (const [name,encoded] of Object.entries(result.fonts)) {
        const bytes=Uint8Array.from(atob(encoded),c=>c.charCodeAt(0));
        const hash=Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',bytes)),c=>c.toString(16).padStart(2,'0')).join('').toUpperCase();
        fonts.set(name,{bytes,hash});
      }
      const assignments={};
      for (const scalar of scalars) {
        const name=result.assignments?.[scalar];
        if (name !== null && !fonts.has(name)) throw Error('Incomplete font coverage response');
        assignments[scalar]=name === null ? null : fonts.get(name).hash;
      }
      if (disposed) return;
      // Publish synchronously after all decoding: no frame can observe half a reply.
      for (const face of result.faces) probe.WorkspaceFont(fonts.get(face.font).bytes,face.bake,-1);
      probe.WorkspaceFontFallbackComplete(JSON.stringify({id,assignments}));
      report(''); wake();
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
  return {frame,dispose(){disposed=true;}};
}
