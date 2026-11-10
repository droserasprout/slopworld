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
  return event => {
    if (!managed.ClipboardTake) return;
    for (const request of JSON.parse(managed.ClipboardTake())) {
      const complete = (text='', error='') => managed.ClipboardComplete(request.id,text,error);
      if (request.kind === 'write' && event?.clipboardData) {
        event.clipboardData.setData('text/plain',request.text); event.preventDefault(); complete();
      } else {
        const operation = request.kind === 'write'
          ? () => navigator.clipboard.writeText(request.text)
          : () => navigator.clipboard.readText();
        try { Promise.resolve(operation()).then(value=>complete(value??''),error=>complete('',failed(error))); }
        catch(error) { complete('',failed(error)); }
      }
    }
  };
}
