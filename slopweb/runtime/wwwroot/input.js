import { acquireTextInput, composingKey } from './text-input.js';

// Pointer Events report a chord's intermediate button changes as pointermove.
// DOM buttons bits are already in Unity order (left, right, middle, back, forward).
const nativeButtons = [0, 2, 1, 3, 4];
export function pointerAction(event) {
    const button = nativeButtons[event.button];
    if (event.type === 'pointermove') {
        const held = event.buttons & 31;
        if (button !== undefined)
            return {kind: held & (1 << button) ? 'down' : 'up', button};
        const dragButton = nativeButtons.find(candidate => held & (1 << candidate));
        return dragButton === undefined ? {kind:'move', button:0} : {kind:'drag', button:dragButton};
    }
    return button === undefined ? null : {kind:event.type === 'pointerdown' ? 'down' : 'up', button};
}

// Browser event source for installed Waylith.Input.InputState. The frame owner
// must drain after BeginFrame and before game Update; no native loop is started.
export function attachInput(canvas, managed, browserClipboard = false) {
    const aliases = { ShiftLeft: 'LeftShift', ShiftRight: 'RightShift',
        ControlLeft: 'LeftCtrl', ControlRight: 'RightCtrl', AltLeft: 'LeftAlt', AltRight: 'RightAlt',
        MetaLeft: 'LeftMeta', MetaRight: 'RightMeta', ArrowLeft: 'LeftArrow', ArrowRight: 'RightArrow',
        ArrowUp: 'UpArrow', ArrowDown: 'DownArrow', BracketLeft: 'LeftBracket', BracketRight: 'RightBracket',
        Equal: 'Equals', NumpadAdd: 'NumpadPlus', NumpadSubtract: 'NumpadMinus', NumpadDecimal: 'NumpadPeriod' };
    const ordinary = /^(Digit[0-9]|F([1-9]|1[0-9]|2[0-4])|Numpad[0-9]|Space|Tab|Enter|Escape|Backspace|Backquote|Quote|Semicolon|Comma|Period|Slash|Backslash|Minus|ContextMenu|Insert|Delete|Home|End|PageUp|PageDown|CapsLock|NumLock|ScrollLock|PrintScreen|Pause|NumpadEnter|NumpadDivide|NumpadMultiply|NumpadEquals)$/;
    const abort = new AbortController();
    const proxy = browserClipboard ? acquireTextInput(canvas) : null;
    let queue = [], disposed = false;
    const enqueue = event => {
        // Overflow must release held state, never leave a lost key-up stuck.
        if (queue.length >= 4096) queue = [{ kind: 'reset' }];
        queue.push(event);
    };
    const listen = (target, type, fn) => target.addEventListener(type, fn, { signal: abort.signal, passive: false });
    const position = event => {
        const rect = canvas.getBoundingClientRect();
        return { x: (event.clientX - rect.left) * canvas.width / Math.max(1, rect.width),
            y: (event.clientY - rect.top) * canvas.height / Math.max(1, rect.height) };
    };
    const listenKeyboard = (type, fn) => {
        if (proxy) proxy.listen(type, fn, {signal:abort.signal, passive:false});
        else listen(canvas, type, fn);
    };
    for (const type of ['keydown', 'keyup']) listenKeyboard(type, event => {
        if (composingKey(event, proxy?.composing)) return;
        const key = aliases[event.code] ?? (/^Key[A-Z]$/.test(event.code) ? event.code.slice(3) : ordinary.test(event.code) ? event.code : null);
        if (!key) return;
        if (!(proxy && event.target === proxy.element) && !(browserClipboard && (event.ctrlKey || event.metaKey) && !event.altKey && event.code === 'KeyV'))
            event.preventDefault();
        enqueue({ kind: event.repeat ? 'repeat' : 'key', key, down: type === 'keydown' });
    });
    for (const type of ['pointerdown', 'pointerup', 'pointermove']) listen(canvas, type, event => {
        if (event.isPrimary === false) return;
        const action = pointerAction(event);
        if (!action) return;
        if (action.kind === 'move' || action.kind === 'drag') {
            enqueue({ kind:'move', ...position(event) });
            return;
        }
        if (type === 'pointerdown') {
            if (!proxy) canvas.focus();
            else if (event.pointerType !== 'touch') proxy.focus(event);
            if (event.isTrusted) canvas.setPointerCapture(event.pointerId);
        }
        if (!(browserClipboard && event.button === 1)) event.preventDefault();
        enqueue({ kind: 'button', button: action.button, down: action.kind === 'down', ...position(event) });
    });
    listen(canvas, 'wheel', event => {
        event.preventDefault();
        // DOM pixel/line/page deltas -> GLFW-style positive-up scroll notches.
        const scale = event.deltaMode === 1 ? 1 / 3 : event.deltaMode === 2 ? 1 : 1 / 100;
        enqueue({ kind: 'scroll', dx: -event.deltaX * scale, dy: -event.deltaY * scale, ...position(event) });
    });
    const reset = () => { queue = [{ kind: 'reset' }]; };
    // A key pressed before preedit can be released while the IME owns keys.
    // Release the physical state when handing ownership to composition.
    listenKeyboard('compositionstart', reset);
    if (proxy) proxy.listen('blur', event => { if (!proxy.owns(event.relatedTarget)) reset(); }, {signal:abort.signal});
    else listen(canvas, 'blur', reset);
    listen(canvas, 'pointercancel', reset);
    listen(window, 'blur', reset);
    listen(document, 'visibilitychange', () => { if (document.hidden) reset(); });
    listen(canvas, 'contextmenu', event => event.preventDefault());
    return {
        frame() { if (!disposed) { const batch = queue; queue = []; managed.InputFrame(JSON.stringify(batch), canvas.height); } },
        dispose() {
            if (disposed) return;
            disposed = true;
            abort.abort();
            proxy?.release();
            queue = [];
            managed.InputFrame('[{"kind":"reset"}]', canvas.height);
        }
    };
}
