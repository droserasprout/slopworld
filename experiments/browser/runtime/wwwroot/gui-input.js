import { renderGui } from './gui.js';
import { clipboardBridge } from './clipboard.js';

export function attachGuiInput(canvas, managed) {
  const abort = new AbortController();
  let queue = [];
  const clipboard = clipboardBridge(managed);
  let clipboardEvent;
  const frame = () => {
    const batch=queue; queue=[];
    const result = JSON.parse(managed.GuiInputFrame(JSON.stringify(batch)));
    canvas.getContext('2d').clearRect(0, 0, canvas.width, canvas.height);
    renderGui(canvas, result.commands);
    clipboard(clipboardEvent);
    return result;
  };
  const listen = (name, callback) => canvas.addEventListener(name, callback, { signal: abort.signal, passive: false });
  const enqueue = event => { if (queue.length >= 4096) queue = [{kind:'reset'}]; queue.push(event); };
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
      if (kind === 'down') { canvas.focus(); if (event.isTrusted) canvas.setPointerCapture(event.pointerId); }
      enqueue({ kind: kind === 'move' && event.buttons ? 'drag' : kind, ...point(event), button: [0, 2, 1][event.button] ?? 0, ...modifiers(event) });
      // Context-menu clipboard actions need the initiating user activation.
      if (managed.ClipboardTake && kind !== 'move') frame();
    });
  }
  listen('keydown', event => {
    if (event.isComposing || ['Dead', 'Process'].includes(event.key)) return;
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
    else if (Array.from(event.key).length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey) text(event.key, event, physical);
    else return;
    event.preventDefault();
  });
  listen('paste', event => {
    if (!managed.ClipboardTake || !event.clipboardData) return;
    event.preventDefault();
    enqueue({kind:'clipboard',text:event.clipboardData.getData('text/plain')});
    enqueue({kind:'key',key:'V',control:true});
    frame();
  });
  listen('copy', event => {
    if (!managed.ClipboardTake) return;
    clipboardEvent=event;
    try { enqueue({kind:'key',key:'C',control:true}); frame(); }
    finally { clipboardEvent=null; }
  });
  listen('compositionend', event => text(event.data, event));
  listen('wheel', event => {
    event.preventDefault();
    const scale = event.deltaMode === 1 ? 1 : event.deltaMode === 2 ? canvas.height / 16 : .03;
    enqueue({kind:'wheel', ...point(event), dx:event.deltaX * scale, dy:event.deltaY * scale, ...modifiers(event)});
  });
  listen('blur', () => { queue = [{ kind: 'reset' }]; });
  listen('pointercancel', () => { queue = [{ kind: 'reset' }]; });
  return {
    frame,
    dispose() { abort.abort(); managed.GuiInputFrame('[{"kind":"reset"}]'); }
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
