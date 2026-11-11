// Browser permissions and ClipboardEvent data stay here; native owners retain
// selection, paste routing, and delayed reply validation.
export function clipboardBridge(managed) {
  let message;
  function failed(error) {
    if (!message) {
      message = document.createElement('div'); message.setAttribute('role', 'alert');
      message.style.cssText = 'position:fixed;bottom:12px;left:12px;padding:12px;background:#402c20;color:white;z-index:1000;max-width:32em';
      document.body.append(message);
    }
    message.textContent = 'Clipboard access failed. Allow clipboard access in your browser, or use Ctrl+V to paste.';
    setTimeout(() => {message?.remove();message=null;}, 8000);
    return String(error);
  }
  async function png(request, blob) {
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
  return event => {
    if (!managed.ClipboardTake) return;
    for (const request of JSON.parse(managed.ClipboardTake())) {
      const complete = (text='', error='') => managed.ClipboardComplete(request.id,text,error);
      if (request.kind === 'write' && event?.clipboardData) {
        event.clipboardData.setData('text/plain',request.text); event.preventDefault(); complete();
      } else {
        const operation = () => {
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
}
