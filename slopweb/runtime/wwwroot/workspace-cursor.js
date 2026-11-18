// Keep one canvas cursor for every animation frame and viewport position.
// Changing CSS cursor images can fall back to the system pointer during decoding.
export function attachWorkspaceCursor(canvas, managed) {
  const previous = canvas.style.cursor;
  const frames = new Map();
  const overlay = document.createElement('canvas');
  overlay.setAttribute('aria-hidden', 'true');
  overlay.style.cssText = 'position:fixed;pointer-events:none;z-index:2147483647;display:none;margin:0!important;max-width:none!important';
  document.body.append(overlay);
  const abort = new AbortController();
  let current, image, point, inside = false;
  function position() {
    const density = globalThis.devicePixelRatio || 1;
    const css = current ? 'none' : previous;
    if (canvas.style.cursor !== css) canvas.style.cursor = css;
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
        if (current) {
          const key=`${current.id}:${current.x}:${current.y}`;
          image = frames.get(key);
          if (!image) {
            const bytes = Uint8ClampedArray.from(atob(current.pixels), c => c.charCodeAt(0));
            const pixels = new ImageData(bytes, current.width, current.height);
            image={pixels};
            if (frames.size >= 32) frames.delete(frames.keys().next().value);
            frames.set(key,image);
          }
          overlay.width = current.width; overlay.height = current.height;
          overlay.getContext('2d').putImageData(image.pixels, 0, 0);
        }
      }
      // Density can change without another native cursor animation frame.
      position();
    },
    dispose() { abort.abort(); overlay.remove(); canvas.style.cursor = previous; frames.clear(); },
  };
}
