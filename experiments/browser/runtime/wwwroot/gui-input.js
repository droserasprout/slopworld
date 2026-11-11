import { acquireTextInput, composingKey } from './text-input.js';
import { renderGui, guiRenderRevision } from './gui.js';
import { GuiFrameReuse } from './gui-frame-reuse.js';
import { decodeGuiQuads } from './gui-quads.js';
import { clipboardBridge } from './clipboard.js';

export function attachGuiInput(canvas, managed) {
  const abort = new AbortController();
  const proxy = acquireTextInput(canvas);
  let disposed = false, composing = false, cancelled = false, pendingText = null, committed = null;
  let queue = [], queuedAt = null;
  const clipboard = clipboardBridge(managed);
  let clipboardEvent;
  const reuse=new GuiFrameReuse();
  const frame = () => {
    if (disposed) return;
    managed.beforeFrame?.();
    const batch=queue; queue=[];
    const inputQueueMs=queuedAt===null?0:performance.now()-queuedAt;queuedAt=null;
    const profiling = managed.profileFrame;
    const started = profiling ? performance.now() : 0;
    const raw = managed.GuiInputFrame(JSON.stringify(batch));
    const managedEnd = profiling ? performance.now() : 0;
    const result = JSON.parse(raw);
    const parsed = profiling ? performance.now() : 0;
    const replay=reuse.prepare(result.commands,canvas.width,canvas.height,guiRenderRevision(),managed.allowFrameReuse===true);
    if(!replay.skip) {
      canvas.getContext('2d').clearRect(0, 0, canvas.width, canvas.height);
      managed.drawBackground?.(canvas.getContext('2d'));
      renderGui(canvas, result.commands);
      reuse.commit(replay.key,canvas.width,canvas.height,guiRenderRevision());
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
  listen('contextlost',()=>reuse.invalidate());
  listen('contextrestored',()=>reuse.invalidate());
  const enqueue = event => {
    if(queue.length===0 && managed.profileFrame)queuedAt=performance.now();
    if (queue.length >= 4096) queue = [{kind:'reset'}];
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
  for (const [name, kind] of [['pointerdown', 'down'], ['pointerup', 'up'], ['pointermove', 'move']]) {
    listen(name, event => {
      event.preventDefault();
      if (kind === 'down') { cancelled = false; proxy.focus(event); if (event.isTrusted) canvas.setPointerCapture(event.pointerId); }
      enqueue({ kind: kind === 'move' && event.buttons ? 'drag' : kind, ...point(event), button: [0, 2, 1][event.button] ?? 0, ...modifiers(event) });
      // Context-menu clipboard actions need the initiating user activation.
      if (managed.ClipboardTake && kind !== 'move') frame();
    });
  }
  listen('keydown', event => {
    pendingText = null;
    if (composingKey(event, composing)) return;
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
      enqueue({kind:'key',key:'C',control:true,shift:event.shiftKey});
      event.preventDefault(); frame(); return;
    }
    const keys = { Backspace: 'Backspace', Delete: 'Delete', Insert:'Insert', ArrowLeft: 'LeftArrow', ArrowRight: 'RightArrow', ArrowUp:'UpArrow', ArrowDown:'DownArrow', PageUp:'PageUp', PageDown:'PageDown', Home: 'Home', End: 'End', Enter: 'Return', Escape:'Escape', Tab:'Tab' };
    const punctuation = {Space:'Space', BracketLeft:'LeftBracket', BracketRight:'RightBracket', Backslash:'Backslash', Minus:'Minus', Backquote:'BackQuote', Equal:'Equals', Semicolon:'Semicolon', Quote:'Quote', Comma:'Comma', Period:'Period', Slash:'Slash'};
    const physical = /^Key[A-Z]$/.test(event.code) ? event.code.slice(3) : /^Digit[0-9]$/.test(event.code) ? 'Alpha' + event.code.slice(5) : punctuation[event.code];
    let key = keys[event.key] ?? (/^F([1-9]|1[0-9]|2[0-4])$/.test(event.key) ? event.key : null);
    if (!key && (event.ctrlKey || event.altKey || event.metaKey))
      key = physical;
    if (key) enqueue({kind:'key', key, ...modifiers(event)});
    else if (Array.from(event.key).length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey) {
      if (editable) { pendingText = {event, key:physical}; return; }
      text(event.key, event, physical);
    }
    else return;
    event.preventDefault();
    // Window commands may require transient user activation. Dispatch native bindings
    // here, before returning from the DOM key event, rather than on the next RAF.
    if (managed.activationFrame) frame();
  });
  listen('paste', event => {
    if (!managed.ClipboardTake || !event.clipboardData) return;
    event.preventDefault();
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
    try { enqueue({kind:'key',key:'C',control:true}); frame(); }
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
    const value = committed;
    queueMicrotask(() => { if (committed === value) committed = null; });
    proxy.element.value = '';
  });
  listen('input', event => {
    if (cancelled || composing || event.isComposing || event.inputType === 'insertCompositionText') return;
    const value = event.data ?? proxy.element.value;
    if (value && value !== committed) text(value, pendingText?.event ?? {}, pendingText?.key);
    pendingText = null; committed = null; proxy.element.value = '';
    if (managed.activationFrame) frame();
  });
  listen('wheel', event => {
    event.preventDefault();
    const scale = event.deltaMode === 1 ? 1 : event.deltaMode === 2 ? canvas.height / 16 : .03;
    enqueue({kind:'wheel', ...point(event), dx:event.deltaX * scale, dy:event.deltaY * scale, ...modifiers(event)});
  });
  const reset = () => {
    composing = false; cancelled = true; pendingText = null; committed = null;
    proxy.element.value = ''; queue = []; queuedAt=null; enqueue({kind:'reset'});
  };
  listen('focus', () => { cancelled = false; });
  listen('blur', event => { if (!proxy.owns(event.relatedTarget)) reset(); });
  listen('pointercancel', reset);
  window.addEventListener('blur', reset, {signal:abort.signal});
  window.addEventListener('focus', () => { cancelled = false; }, {signal:abort.signal});
  document.addEventListener('visibilitychange', () => { if (document.hidden) reset(); }, {signal:abort.signal});
  return {
    frame,
    textInput: proxy.element,
    dispose() { if (disposed) return; disposed = true; abort.abort(); proxy.release(); managed.GuiInputFrame('[{"kind":"reset"}]'); }
  };
}

export async function verifyGuiInput(managed) {
  const canvas = document.createElement('canvas'); canvas.width = 640; canvas.height = 200; canvas.tabIndex = 0;
  document.body.prepend(canvas);
  const input = attachGuiInput(canvas, managed);
  const point = (type, x, y) => {
    const rect = canvas.getBoundingClientRect();
    canvas.dispatchEvent(new PointerEvent(type, { button: 0, clientX: rect.left + x, clientY: rect.top + y }));
  };
  const click = (x, y) => { point('pointerdown', x, y); input.frame(); point('pointerup', x, y); return input.frame(); };
  const key = (key, extra = {}) => canvas.dispatchEvent(new KeyboardEvent('keydown', { key, ...extra }));
  try {
    input.frame();
    let state = click(60, 65);
    if (state.clicks !== 1) throw new Error(`GUI button did not click: ${JSON.stringify(state)}`);
    state = click(600, 180);
    if (state.clicks !== 1) throw new Error('Outside click activated button');
    click(60, 125);
    for (const character of 'agent') { key(character,{code:'Key'+character.toUpperCase()}); input.frame(); }
    key('Backspace'); state = input.frame();
    if (state.text !== 'agen') throw new Error(`GUI editing failed: ${state.text}`);
    key('💡'); state = input.frame();
    if (state.text !== 'agen💡') throw new Error('UTF-16 input batch lost a supplementary character');
    key('@', {code:'KeyQ',ctrlKey:true,altKey:true,modifierAltGraph:true}); state=input.frame();
    if(state.text !== 'agen💡@') throw new Error('AltGr text was interpreted as a native shortcut');
    key('a', {code:'KeyA', ctrlKey:true}); input.frame();
    for (const character of 'agen') key(character);
    state = input.frame();
    if (state.text !== 'agen') throw new Error('Control-key selection did not reach the native field');
    point('pointerdown', 60, 65); input.frame(); canvas.dispatchEvent(new Event('blur')); input.frame();
    point('pointerup', 60, 65); state = input.frame();
    if (state.clicks !== 1) throw new Error('Focus loss retained pressed button');
    return { clicks: state.clicks, text: state.text, syntheticDomEvents: true };
  } finally { input.dispose(); }
}
