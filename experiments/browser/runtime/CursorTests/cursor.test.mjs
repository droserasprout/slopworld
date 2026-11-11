import assert from 'node:assert/strict';
import { test } from 'node:test';
import { attachWorkspaceCursor } from '../wwwroot/workspace-cursor.js';

test('native RGBA and hotspot survive cursor presentation, reuse and disposal', () => {
  const uploads = [];
  const oldDocument = globalThis.document, oldImageData = globalThis.ImageData;
  let encodes = 0;
  globalThis.ImageData = class {
    constructor(bytes, width, height) { Object.assign(this, { bytes, width, height }); }
  };
  globalThis.document = { createElement: () => ({
    getContext: () => ({ putImageData: image => uploads.push(image) }),
    toDataURL: () => `data:image/png;base64,frame${++encodes}`,
  }) };
  try {
    const canvas = { style: { cursor: 'crosshair' } };
    let pending = '';
    const adapter = attachWorkspaceCursor(canvas, { WorkspaceCursorTake: () => {
      const value = pending; pending = ''; return value;
    } });
    const frame = { id: 1, width: 1, height: 2, pixels: 'AQIDBAUGBwg=', x: 0, y: 1 };
    pending = JSON.stringify(frame);
    adapter.frame();
    assert.deepEqual([...uploads[0].bytes], [1, 2, 3, 4, 5, 6, 7, 8]);
    assert.equal(uploads[0].width, 1); assert.equal(uploads[0].height, 2);
    assert.equal(canvas.style.cursor, 'url("data:image/png;base64,frame1") 0 1, auto');
    pending = JSON.stringify({ ...frame, x: 0.4, y: 0.6 });
    adapter.frame(); adapter.frame();
    assert.equal(encodes, 1);
    pending = 'null'; adapter.frame();
    assert.equal(canvas.style.cursor, 'crosshair');
    pending = JSON.stringify({ ...frame, id: 2 }); adapter.frame();
    adapter.dispose();
    assert.equal(canvas.style.cursor, 'crosshair');
  } finally {
    globalThis.document = oldDocument; globalThis.ImageData = oldImageData;
  }
});
