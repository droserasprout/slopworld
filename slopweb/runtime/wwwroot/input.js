import { acquireTextInput, composingKey } from './text-input.js';
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
    listen(canvas, 'pointermove', event => enqueue({ kind: 'move', ...position(event) }));
    for (const type of ['pointerdown', 'pointerup']) listen(canvas, type, event => {
        if (event.button < 0 || event.button > 4) return;
        if (type === 'pointerdown') {
            proxy ? proxy.focus(event) : canvas.focus();
            if (event.isTrusted) canvas.setPointerCapture(event.pointerId);
        }
        event.preventDefault();
        enqueue({ kind: 'button', button: [0, 2, 1, 3, 4][event.button], down: type === 'pointerdown', ...position(event) });
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
