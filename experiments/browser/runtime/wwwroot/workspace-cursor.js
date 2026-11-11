// Present native GameCursor pixels at the native hotspot, independently of
// canvas backing size and the game's UI scale.
export function attachWorkspaceCursor(canvas, managed) {
  const previous = canvas.style.cursor;
  const frames = new Map();
  return {
    frame() {
      const update = managed.WorkspaceCursorTake();
      if (!update) return;
      const cursor = JSON.parse(update);
      if (!cursor) { canvas.style.cursor = previous; return; }
      let url = frames.get(cursor.id);
      if (!url) {
        const image = document.createElement('canvas');
        image.width = cursor.width; image.height = cursor.height;
        const bytes = Uint8ClampedArray.from(atob(cursor.pixels), c => c.charCodeAt(0));
        image.getContext('2d').putImageData(new ImageData(bytes, cursor.width, cursor.height), 0, 0);
        url = image.toDataURL('image/png');
        // Preference changes create new native textures; bound retained data URLs.
        if (frames.size >= 32) frames.delete(frames.keys().next().value);
        frames.set(cursor.id, url);
      }
      canvas.style.cursor = `url("${url}") ${Math.round(cursor.x)} ${Math.round(cursor.y)}, auto`;
    },
    dispose() { canvas.style.cursor = previous; frames.clear(); },
  };
}
