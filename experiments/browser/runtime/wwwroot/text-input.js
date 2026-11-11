// Shared editable DOM ownership. GUI events own text commits; device input only
// observes physical keys. Neither adapter may steal focus during their handoff.
const proxies = new WeakMap();
export function acquireTextInput(canvas) {
  let shared = proxies.get(canvas);
  if (!shared) {
    const element = document.createElement('textarea');
    element.dataset.slopworldTextInput = '';
    element.setAttribute('aria-label', 'SlopWorld keyboard input');
    element.autocomplete = 'off'; element.spellcheck = false;
    element.setAttribute('autocapitalize', 'off');
    element.tabIndex = -1;
    // An editable, rendered element is required by Chromium's IME. The pointer
    // anchor is approximate: native GUI caret geometry is not available here.
    element.style.cssText = 'position:fixed;left:0;top:0;width:1px;height:1px;padding:0;border:0;opacity:0;pointer-events:none;resize:none';
    canvas.after(element);
    const abort = new AbortController();
    shared = {element, references:0, composing:false};
    const listen = (target, type, callback) => target.addEventListener(type, callback, {signal:abort.signal});
    shared.focus = event => {
      if (event?.clientX !== undefined) {
        element.style.left = `${event.clientX}px`; element.style.top = `${event.clientY}px`;
      }
      element.focus({preventScroll:true});
    };
    listen(canvas, 'focus', () => shared.focus());
    listen(element, 'compositionstart', () => { shared.composing = true; });
    listen(element, 'compositionend', () => { shared.composing = false; });
    listen(element, 'blur', () => { shared.composing = false; element.value = ''; });
    shared.destroy = () => { abort.abort(); element.remove(); proxies.delete(canvas); };
    proxies.set(canvas, shared);
    if (document.activeElement === canvas) shared.focus();
  }
  shared.references++;
  let released = false;
  return {
    element: shared.element,
    get composing() { return shared.composing; },
    focus: shared.focus,
    owns: target => target === canvas || target === shared.element,
    listen(type, callback, options) {
      canvas.addEventListener(type, callback, options);
      shared.element.addEventListener(type, callback, options);
    },
    release() { if (!released) { released = true; if (--shared.references === 0) shared.destroy(); } },
  };
}

export function composingKey(event, composing = false) {
  return composing || event.isComposing || event.keyCode === 229 || ['Dead', 'Process'].includes(event.key);
}
