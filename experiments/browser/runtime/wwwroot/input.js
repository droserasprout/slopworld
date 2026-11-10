// Browser event source for installed Waylith.Input.InputState. The frame owner
// must drain after BeginFrame and before game Update; no native loop is started.
export function attachInput(canvas, managed) {
    const aliases = { ShiftLeft: 'LeftShift', ShiftRight: 'RightShift',
        ControlLeft: 'LeftCtrl', ControlRight: 'RightCtrl', AltLeft: 'LeftAlt', AltRight: 'RightAlt',
        MetaLeft: 'LeftMeta', MetaRight: 'RightMeta', ArrowLeft: 'LeftArrow', ArrowRight: 'RightArrow',
        ArrowUp: 'UpArrow', ArrowDown: 'DownArrow', BracketLeft: 'LeftBracket', BracketRight: 'RightBracket',
        Equal: 'Equals', NumpadAdd: 'NumpadPlus', NumpadSubtract: 'NumpadMinus', NumpadDecimal: 'NumpadPeriod' };
    const ordinary = /^(Digit[0-9]|F([1-9]|1[0-9]|2[0-4])|Numpad[0-9]|Space|Tab|Enter|Escape|Backspace|Backquote|Quote|Semicolon|Comma|Period|Slash|Backslash|Minus|ContextMenu|Insert|Delete|Home|End|PageUp|PageDown|CapsLock|NumLock|ScrollLock|PrintScreen|Pause|NumpadEnter|NumpadDivide|NumpadMultiply|NumpadEquals)$/;
    const abort = new AbortController();
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
    for (const type of ['keydown', 'keyup']) listen(canvas, type, event => {
        const key = aliases[event.code] ?? (/^Key[A-Z]$/.test(event.code) ? event.code.slice(3) : ordinary.test(event.code) ? event.code : null);
        if (!key) return;
        event.preventDefault();
        enqueue({ kind: event.repeat ? 'repeat' : 'key', key, down: type === 'keydown' });
    });
    listen(canvas, 'pointermove', event => enqueue({ kind: 'move', ...position(event) }));
    for (const type of ['pointerdown', 'pointerup']) listen(canvas, type, event => {
        if (event.button < 0 || event.button > 4) return;
        if (type === 'pointerdown') {
            canvas.focus();
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
    listen(canvas, 'blur', reset);
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
            queue = [];
            managed.InputFrame('[{"kind":"reset"}]', canvas.height);
        }
    };
}

export function verifyInput(managed) {
    const canvas = document.createElement('canvas');
    canvas.width = 800; canvas.height = 400; canvas.tabIndex = 0;
    canvas.style.cssText = 'position:fixed;left:0;top:0;width:400px;height:200px';
    document.body.append(canvas);
    const input = attachInput(canvas, managed);
    const checks = [];
    const check = (name, condition) => { if (!condition) throw new Error(`${name}: ${managed.InputSnapshot('A')}`); checks.push(name); };
    const snapshot = () => JSON.parse(managed.InputSnapshot('A'));
    const key = (type, code, repeat = false) => canvas.dispatchEvent(new KeyboardEvent(type, { code, repeat }));
    try {
        canvas.focus();
        key('keydown', 'ShiftLeft'); key('keydown', 'KeyA'); input.frame();
        let state = snapshot();
        check('installed Waylith and Unity key-down', state.down && state.pressed && state.unityDown && state.shift);
        key('keydown', 'KeyA', true); input.frame(); state = snapshot();
        check('held key survives frame; repeat is not a new press', state.down && !state.pressed);
        key('keyup', 'KeyA'); input.frame(); state = snapshot();
        check('release reaches installed input', !state.down && state.released && !state.unityDown);
        const rect = canvas.getBoundingClientRect();
        const at = { clientX: rect.left + 40, clientY: rect.top + 30 };
        canvas.dispatchEvent(new PointerEvent('pointerdown', { ...at, button: 2 }));
        canvas.dispatchEvent(new WheelEvent('wheel', { ...at, deltaY: 3, deltaMode: 1 }));
        input.frame(); state = snapshot();
        check('framebuffer coordinates, right-button mapping and wheel', state.x === 80 && state.y === 60 && state.unityX === 80 && state.unityY === 340 && state.rightButton && state.scrollY === -1);
        key('keydown', 'KeyA'); canvas.dispatchEvent(new Event('blur')); input.frame(); state = snapshot();
        check('focus loss clears queued and held input', !state.down && !state.shift && !state.rightButton);
        key('keydown', 'KeyA'); input.frame(); input.dispose(); input.dispose();
        key('keydown', 'KeyA'); input.frame();
        check('dispose releases keys and detaches listeners', !snapshot().down);
        return { status: 'pass', checks, source: 'installed Waylith.Input.InputState and UnityEngine.Input', syntheticDomEvents: true };
    } finally { input.dispose(); canvas.remove(); }
}
