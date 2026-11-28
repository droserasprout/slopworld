// Browser permissions and ClipboardEvent data stay here; native owners retain
// selection, paste routing, and delayed reply validation.
const maxPngBytes = 20 * 1024 * 1024; // slopd's image upload limit.

export function clipboardBridge(managed, selectPrimary) {
  let message, primaryRequest;
  function failed(error) {
    if (!message) {
      message = document.createElement('div'); message.setAttribute('role', 'alert');
      message.style.cssText = 'position:fixed;bottom:12px;left:12px;padding:12px;background:#402c20;color:white;z-index:1000;max-width:32em';
      document.body.append(message);
    }
    const reason = error?.message || String(error);
    message.textContent = error?.name === 'NotAllowedError'
      ? 'Clipboard access denied. Allow clipboard access in your browser.'
      : 'Clipboard operation failed. ' + reason;
    setTimeout(() => {message?.remove();message=null;}, 8000);
    return reason;
  }
  async function png(request, blob) {
    if (blob.size > maxPngBytes) throw new Error('Clipboard PNG exceeds the 20 MiB limit.');
    if (!managed.ClipboardCompleteImage) throw new Error('Clipboard image export unavailable');
    const bytes = new Uint8Array(await blob.arrayBuffer());
    // Avoid argument-count limits on large clipboard images.
    let binary = '';
    for (let offset=0; offset<bytes.length; offset+=8192)
      binary += String.fromCharCode(...bytes.subarray(offset,offset+8192));
    managed.ClipboardCompleteImage(request.id,btoa(binary),'');
  }
  async function read(request, complete) {
    if (!request.images) { complete(await navigator.clipboard.readText()); return; }
    const items = await navigator.clipboard.read();
    // Match native policy: Codex tries text first, even when an image is offered.
    for (const item of items) {
      if (!item.types.includes('text/plain')) continue;
      const text = await (await item.getType('text/plain')).text();
      if (text) { complete(text); return; }
    }
    for (const item of items) {
      if (item.types.includes('image/png')) {
        await png(request,await item.getType('image/png')); return;
      }
    }
    complete();
  }
  const bridge = event => {
    if (!managed.ClipboardTake) return;
    for (const request of JSON.parse(managed.ClipboardTake())) {
      const complete = (text='', error='') => managed.ClipboardComplete(request.id,text,error);
      if (request.kind === 'readPrimary') {
        bridge.cancelPrimary(); primaryRequest=request; continue;
      }
      if (request.kind === 'write' && event?.clipboardData) {
        event.clipboardData.setData('text/plain',request.text); event.preventDefault(); complete();
      } else {
        const operation = () => {
          // Linux browsers publish editable DOM selections to PRIMARY. The
          // Clipboard API writes CLIPBOARD instead and must not be used here.
          if (request.kind === 'primary') {
            selectPrimary(request.text); complete(); return;
          }
          if (request.kind === 'eventImage') {
            // Capture the File before returning from the trusted paste event;
            // ClipboardEvent's data store need not remain readable afterwards.
            const item = Array.from(event?.clipboardData?.items ?? [])
              .find(item=>item.kind === 'file' && item.type === 'image/png');
            const blob = item?.getAsFile();
            if (!blob) throw new Error('Paste event has no PNG');
            return png(request,blob);
          }
          if (request.kind === 'write') return navigator.clipboard.writeText(request.text).then(()=>complete());
          return read(request,complete);
        };
        try { Promise.resolve(operation()).catch(error=>complete('',failed(error))); }
        catch(error) { complete('',failed(error)); }
      }
    }
  };
  Object.defineProperty(bridge,'primaryPending',{get:()=>!!primaryRequest});
  bridge.cancelPrimary = () => {
    if (!primaryRequest) return;
    const request=primaryRequest; primaryRequest=null;
    managed.ClipboardComplete(request.id,'','PRIMARY paste canceled');
  };
  bridge.expirePrimary = () => {
    const request = primaryRequest;
    if (request) setTimeout(() => {
      if (primaryRequest === request) bridge.cancelPrimary();
    }, 0);
  };
  bridge.pastePrimary = event => {
    if (!primaryRequest) return false;
    const request=primaryRequest; primaryRequest=null;
    managed.ClipboardComplete(request.id,event.clipboardData.getData('text/plain'),'');
    return true;
  };
  return bridge;
}
