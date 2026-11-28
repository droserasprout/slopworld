import { acquireTextInput, composingKey } from './text-input.js';
import { pointerAction } from './input.js';
import { renderGui, guiRenderRevision, takeGuiTargetInvalidation } from './gui.js';
import { GuiFrameReuse, GuiPacketDecoder } from './gui-frame-reuse.js';
import { decodeGuiQuads } from './gui-quads.js';
import { clipboardBridge } from './clipboard.js';

export function attachGuiInput(canvas, managed) {
  const abort = new AbortController();
  const proxy = acquireTextInput(canvas);
  let disposed = false, composing = false, cancelled = false, pendingText = null, committed = null;
  let commitTimer, capturedPointer;
  let queue = [], queuedAt = null;
  const clipboard = clipboardBridge(managed, text => {
    if (proxy.composing) throw new Error('Cannot publish selection during composition');
    proxy.element.value = text;
    proxy.focus();
    // Firefox ignores Selection API changes for PRIMARY. Its editor command
    // performs a real select-all without overwriting the ordinary clipboard.
    if (!document.execCommand?.('selectAll')) proxy.element.select();
  });
  let clipboardEvent;
  const reuse=new GuiFrameReuse();
  const decoder=new GuiPacketDecoder();
  const frame = () => {
    if (disposed) return;
    managed.beforeFrame?.();
    const batch=queue; queue=[];
    if (takeGuiTargetInvalidation()) {
      // Deliver restoration before input or repaint, even after a full input queue.
      batch.unshift({kind:'restoreTargets'});
      decoder.previous = null;
      reuse.invalidate();
    }
    const inputQueueMs=queuedAt===null?0:performance.now()-queuedAt;queuedAt=null;
    const profiling = managed.profileFrame;
    const started = profiling ? performance.now() : 0;
    const raw = managed.GuiInputFrame(JSON.stringify(batch),decoder.packetId);
    const managedEnd = profiling ? performance.now() : 0;
    const result = {...decoder.decode(raw)};
    const parsed = profiling ? performance.now() : 0;
    // Profile timings change every frame even when commands do not. Compare only
    // commands in that mode; ordinary frames can use their existing wire identity.
    const packet = result.profile ? null : decoder.previous?.raw ?? raw;
    const replay = reuse.prepare(result.commands, canvas.width, canvas.height,
      guiRenderRevision(), managed.allowFrameReuse === true, packet);
    if(!replay.skip) {
      canvas.getContext('2d').clearRect(0, 0, canvas.width, canvas.height);
      managed.drawBackground?.(canvas.getContext('2d'));
      renderGui(canvas, result.commands);
      reuse.commit(replay.key,canvas.width,canvas.height,guiRenderRevision(),result.commands);
    }
    const painted = profiling ? performance.now() : 0;
    result.presentationUnchanged=replay.skip;
    clipboard(clipboardEvent);
    if (profiling) profiling({ ...result.profile, managedMs: managedEnd-started,
      parseMs: parsed-managedEnd, canvasMs: painted-parsed, clipboardMs: performance.now()-painted,
      commands: result.commands.length,
      canvasReused: replay.skip?1:0,
      targetWrites: result.commands.filter(c => c.kind === 'target' && c.value.id).length,
      targetBlits: result.commands.filter(c => c.kind === 'targetTexture').length,
      imageQuads: result.commands.reduce((count,c) => count + (c.kind === 'imageBatch' ? decodeGuiQuads(c.value.quads).length/8 : c.kind === 'imageTexture' ? 1 : 0), 0),
      inputEvents: batch.length, inputQueueMs });
    return result;
  };
  const listen = (name, callback) => proxy.listen(name, callback, { signal: abort.signal, passive: false });
  const enqueue = event => {
    if(queue.length===0 && managed.profileFrame)queuedAt=performance.now();
    // Reserve one final reset so focus loss cannot overflow a full accepted batch.
    if (queue.length >= 4096 && event.kind !== 'reset') queue = [{kind:'reset'}];
    queue.push(event);
  };
  const modifiers = event => ({ shift: event.shiftKey, control: event.ctrlKey, alt: event.altKey, command: event.metaKey });
  const text = (value, event, key) => {
    // Numeric UTF-16 units survive JSON individually; the mod flushes the whole
    // ordered batch so supplementary characters reach its original input buffer.
    // Preserve a physical identity on the same event for rebindable shortcuts,
    // without creating a second text-bearing event or duplicating terminal input.
    for (let i = 0; i < value.length; i++) enqueue({kind:'text', character:value.charCodeAt(i), ...(i === 0 && key ? {key} : {}), ...modifiers(event)});
  };
  const point = event => {
    const rect = canvas.getBoundingClientRect();
    return { x: (event.clientX - rect.left) * canvas.width / rect.width,
      y: (event.clientY - rect.top) * canvas.height / rect.height };
  };
  for (const name of ['pointerdown', 'pointerup', 'pointermove']) {
    listen(name, event => {
      if (event.isPrimary === false) return;
      const action = pointerAction(event);
      // The device adapter exposes thumb buttons. Native GUI controls only
      // support the first three, and some clamp higher buttons during hit testing.
      if (!action || action.button > 2) return;
      const {kind, button} = action;
      const transition = kind === 'down' || kind === 'up';
      const middle = managed.ClipboardTake && button === 2 && transition;
      if (kind === 'down') clipboard.cancelPrimary();
      if (!middle) event.preventDefault();
      // A touch on a Board handle or scroll surface must keep the keyboard closed.
      // Native hit testing runs synchronously while the gesture can still focus an editor.
      const touchFocus = kind === 'down' && event.pointerType === 'touch' && managed.touchKeyboard;
      if (kind === 'down') { capturedPointer = event.pointerId; cancelled = false; if (!touchFocus) proxy.focus(event); if (event.isTrusted && name === 'pointerdown') canvas.setPointerCapture(event.pointerId); }
      if (name === 'pointerup') capturedPointer = undefined;
      enqueue({kind, button, pointerType:event.pointerType, ...point(event), ...modifiers(event)});
      // Clipboard actions and native URL opening need the initiating user activation.
      if (touchFocus || (managed.ClipboardTake && transition)) frame();
      if (touchFocus && managed.touchKeyboard()) proxy.focus(event);
      if (middle && !clipboard.primaryPending) event.preventDefault();
    });
  }
  // The browser may dispatch PRIMARY paste as the default action of auxclick.
  // Release an unanswered read after that action, or before any new key gesture.
  listen('auxclick', event => {
    if (event.button === 1) clipboard.expirePrimary();
  });
  listen('keydown', event => {
    clipboard.cancelPrimary();
    pendingText = null;
    if (composingKey(event, composing)) return;
    committed = null;
    cancelled = false;
    const editable = event.target === proxy.element && event.isTrusted;
    // AltGr is text production, even on platforms exposing it as Ctrl+Alt.
    // Do not carry those synthetic modifiers or a binding key into native text
    // dispatch: they would turn e.g. AltGr+Q's @ into a terminal shortcut.
    if (event.getModifierState?.('AltGraph') && !event.metaKey && Array.from(event.key).length === 1) {
      const modifiers = {shiftKey:event.shiftKey,ctrlKey:false,altKey:false,metaKey:false};
      if (editable) { pendingText = {event:modifiers}; return; }
      text(event.key, modifiers);
      event.preventDefault();
      return;
    }
    const shortcut = (event.ctrlKey || event.metaKey) && !event.altKey;
    // Let the browser deliver ClipboardEvent data. Sending V now would race
    // that event and could paste a previous native clipboard value twice.
    if (managed.ClipboardTake && shortcut && event.code === 'KeyV') return;
    if (managed.ClipboardTake && shortcut && event.code === 'KeyC') {
      // Command+C requests copy only. Native Ctrl+Shift+C copies a selection
      // and leaves a terminal with no selection alone.
      enqueue({kind:'key',key:'C',control:true,shift:event.metaKey || event.shiftKey});
      event.preventDefault(); frame(); return;
    }
    const keys = { Backspace: 'Backspace', Delete: 'Delete', Insert:'Insert', ArrowLeft: 'LeftArrow', ArrowRight: 'RightArrow', ArrowUp:'UpArrow', ArrowDown:'DownArrow', PageUp:'PageUp', PageDown:'PageDown', Home: 'Home', End: 'End', Enter: 'Return', Escape:'Escape', Tab:'Tab' };
    const punctuation = {Space:'Space', BracketLeft:'LeftBracket', BracketRight:'RightBracket', Backslash:'Backslash', Minus:'Minus', Backquote:'BackQuote', Equal:'Equals', Semicolon:'Semicolon', Quote:'Quote', Comma:'Comma', Period:'Period', Slash:'Slash'};
    const keypad = /^Numpad[0-9]$/.test(event.code) ? 'Keypad' + event.code.slice(6) : null;
    let physical = keypad ?? punctuation[event.code];
    if (/^Key[A-Z]$/.test(event.code)) physical = event.code.slice(3);
    else if (/^Digit[0-9]$/.test(event.code)) physical = 'Alpha' + event.code.slice(5);
    let key = keys[event.key] ?? (/^F([1-9]|1[0-9]|2[0-4])$/.test(event.key) ? event.key : null);
    // Slot shortcuts use the physical keypad even if Num Lock maps it to navigation.
    if ((event.altKey && keypad) || (!key && (event.ctrlKey || event.altKey || event.metaKey))) key = physical;
    const candidate = key ?? physical;
    const activation = !event.shiftKey && !!candidate && managed.activationKey?.(candidate) === true;
    if (activation && candidate === 'F11' && event.repeat) {
      event.preventDefault(); return;
    }
    if (key) enqueue({kind:'key', key, ...modifiers(event)});
    else if (Array.from(event.key).length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey) {
      // A printable fullscreen binding needs activation now. Deliver one event
      // with both its key and its character so modal fields can still type it.
      if (editable && !activation) { pendingText = {event, key:physical}; return; }
      text(event.key, event, physical);
    }
    else if (activation) enqueue({kind:'key', key:candidate, ...modifiers(event)});
    else return;
    event.preventDefault();
    // Window commands may require transient user activation. Dispatch native bindings
    // here, before returning from the DOM key event, rather than on the next RAF.
    if (activation) frame();
  });
  listen('paste', event => {
    if (!managed.ClipboardTake || !event.clipboardData) return;
    event.preventDefault();
    if (clipboard.pastePrimary(event)) return;
    enqueue({kind:'clipboard',text:event.clipboardData.getData('text/plain'),
      image:Array.from(event.clipboardData.items).some(item=>item.kind === 'file' && item.type === 'image/png')});
    enqueue({kind:'key',key:'V',control:true});
    clipboardEvent=event;
    try { frame(); }
    finally { clipboardEvent=null; }
  });
  listen('copy', event => {
    if (!managed.ClipboardTake) return;
    clipboardEvent=event;
    try { enqueue({kind:'key',key:'C',control:true,shift:true}); frame(); }
    finally { clipboardEvent=null; }
  });
  listen('compositionstart', () => { composing = true; cancelled = false; pendingText = null; committed = null; });
  listen('compositionend', event => {
    // Refocus permits fresh typing, but cannot revive preedit canceled on blur.
    const accepted = composing && !cancelled;
    composing = false; pendingText = null;
    if (accepted && event.data) text(event.data, {});
    // Suppress the same-operation trailing input even for a rejected commit.
    committed = event.data;
    // Some engines send the final insertText after compositionend in the same
    // edit operation. Ignore that duplicate, but allow a later identical commit.
    // Native event dispatch may run microtasks before the trailing input.
    clearTimeout(commitTimer);
    commitTimer = setTimeout(() => { committed = null; }, 0);
    proxy.element.value = '';
  });
  listen('input', event => {
    if (cancelled || composing || event.isComposing || event.inputType === 'insertCompositionText') return;
    const value = event.data ?? proxy.element.value;
    if (value && value !== committed) text(value, pendingText?.event ?? {}, pendingText?.key);
    pendingText = null; committed = null; proxy.element.value = '';
  });
  listen('wheel', event => {
    event.preventDefault();
    const scale = event.deltaMode === 1 ? 1 : event.deltaMode === 2 ? canvas.height / 16 : .03;
    enqueue({kind:'wheel', ...point(event), dx:event.deltaX * scale, dy:event.deltaY * scale, ...modifiers(event)});
  });
  const reset = () => {
    capturedPointer = undefined;
    composing = false; cancelled = true; pendingText = null; committed = null;
    if (proxy.composing) proxy.element.value = '';
    clipboard.cancelPrimary();
    // Preedit is canceled above; committed events must reach their native owner
    // before focus is cleared. Window blur and visibility can report the same loss.
    if (queue.at(-1)?.kind !== 'reset') enqueue({kind:'reset'});
  };
  listen('focus', () => { cancelled = false; });
  listen('blur', event => { if (!proxy.owns(event.relatedTarget)) reset(); });
  listen('pointercancel', reset);
  listen('lostpointercapture', event => { if (capturedPointer !== undefined && capturedPointer === event.pointerId) reset(); });
  window.addEventListener('blur', reset, {signal:abort.signal});
  window.addEventListener('focus', () => { cancelled = false; }, {signal:abort.signal});
  document.addEventListener('visibilitychange', () => { if (document.hidden) reset(); }, {signal:abort.signal});
  return {
    frame,
    textInput: proxy.element,
    dispose() { if (disposed) return; disposed = true; clearTimeout(commitTimer); clipboard.cancelPrimary(); abort.abort(); proxy.release(); managed.GuiInputFrame('[{"kind":"reset"}]'); }
  };
}
