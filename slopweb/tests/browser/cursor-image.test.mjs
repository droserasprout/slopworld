import assert from 'node:assert/strict';
import { test } from 'node:test';
import { attachWorkspaceCursor } from '../../runtime/wwwroot/workspace-cursor.js';

test('native RGBA and hotspot survive cursor presentation, reuse and disposal', () => {
  const uploads = [];
  const keys = ['document', 'ImageData', 'addEventListener', 'CSS', 'devicePixelRatio'];
  const previous = new Map(keys.map(key => [key, Object.getOwnPropertyDescriptor(globalThis, key)]));
  let encodes = 0;
  const overlay = {
    style: { setProperty(key, value) { this[key] = value; } },
    setAttribute() {},
    getContext: () => ({ putImageData: image => uploads.push(image) }),
    toDataURL: () => { encodes++; return 'data:image/png;base64,AQID'; },
    remove() { this.removed = true; },
  };
  globalThis.ImageData = class {
    constructor(bytes, width, height) { Object.assign(this, { bytes, width, height }); }
  };
  globalThis.document = { createElement: () => overlay, body: { append() {} } };
  globalThis.addEventListener = () => {};
  globalThis.CSS = { supports: () => true };
  globalThis.devicePixelRatio = 1;
  try {
    const canvas = new EventTarget();
    canvas.style = { cursor: 'crosshair' };
    let pending = '';
    const adapter = attachWorkspaceCursor(canvas, { WorkspaceCursorTake: () => {
      const value = pending; pending = ''; return value;
    } });
    const frame = { id: 1, width: 1, height: 2, pixels: 'AQIDBAUGBwg=', x: 0, y: 1 };
    pending = JSON.stringify(frame);
    adapter.frame();
    assert.deepEqual([...uploads[0].bytes], [1, 2, 3, 4, 5, 6, 7, 8]);
    assert.equal(uploads[0].width, 1); assert.equal(uploads[0].height, 2);
    const encoded = canvas.style.cursor.match(/^image-set\(url\("data:image\/x-icon;base64,([^"]+)"\) 1x\), auto$/);
    assert.ok(encoded);
    const header = Buffer.from(encoded[1], 'base64');
    assert.equal(header.readUInt16LE(10), 0);
    assert.equal(header.readUInt16LE(12), 1);
    pending = JSON.stringify(frame);
    adapter.frame(); adapter.frame();
    assert.equal(encodes, 1);
    pending = 'null'; adapter.frame();
    assert.equal(canvas.style.cursor, 'crosshair');
    pending = JSON.stringify({ ...frame, id: 2 }); adapter.frame();
    assert.equal(encodes, 2);
    adapter.dispose();
    assert.equal(canvas.style.cursor, 'crosshair');
    assert.equal(overlay.removed, true);
  } finally {
    for (const [key, value] of previous) {
      if (value) Object.defineProperty(globalThis, key, value);
      else delete globalThis[key];
    }
  }
});
