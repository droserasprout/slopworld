// Native cursor pixels and hotspot are device pixels, independent of UI scale.
// A pointer-transparent overlay avoids Chromium's custom-cursor rejection when
// its bitmap would cross the viewport edge. The viewport clips only the ink.
export function attachWorkspaceCursor(canvas, managed) {
  const previous = canvas.style.cursor;
  const frames = new Map();
  const overlay = document.createElement('canvas');
  overlay.setAttribute('aria-hidden', 'true');
  overlay.style.cssText = 'position:fixed;pointer-events:none;z-index:2147483647;display:none;margin:0!important;max-width:none!important';
  document.body.append(overlay);
  const abort = new AbortController();
  let current, point, inside = false;
  function position() {
    const density = globalThis.devicePixelRatio || 1;
    overlay.style.display = current && inside ? 'block' : 'none';
    if (!current || !point) return;
    overlay.style.setProperty('width', `${current.width / density}px`, 'important');
    overlay.style.setProperty('height', `${current.height / density}px`, 'important');
    overlay.style.left = `${point.x - current.x / density}px`;
    overlay.style.top = `${point.y - current.y / density}px`;
  }
  canvas.addEventListener('pointermove', event => {
    point = {x:event.clientX, y:event.clientY}; inside = true; position();
  }, {signal:abort.signal});
  canvas.addEventListener('pointerleave', () => {inside=false;position();}, {signal:abort.signal});
  globalThis.addEventListener('blur', () => {inside=false;position();}, {signal:abort.signal});
  return {
    frame() {
      const update = managed.WorkspaceCursorTake();
      if (update) {
        current = JSON.parse(update);
        canvas.style.cursor = current ? 'none' : previous;
        if (current) {
          let pixels = frames.get(current.id);
          if (!pixels) {
            const bytes = Uint8ClampedArray.from(atob(current.pixels), c => c.charCodeAt(0));
            pixels = new ImageData(bytes, current.width, current.height);
            if (frames.size >= 32) frames.delete(frames.keys().next().value);
            frames.set(current.id, pixels);
          }
          overlay.width = current.width; overlay.height = current.height;
          overlay.getContext('2d').putImageData(pixels, 0, 0);
        }
      }
      // Density can change without another native cursor animation frame.
      position();
    },
    dispose() { abort.abort(); overlay.remove(); canvas.style.cursor = previous; frames.clear(); },
  };
}
