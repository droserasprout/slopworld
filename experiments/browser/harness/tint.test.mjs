import test from 'node:test';
import assert from 'node:assert/strict';
import { tintPixels } from '../runtime/wwwroot/gui-tint.js';

test('tint pixels retain the original byte rounding, alpha and source', () => {
  const pixels = Uint8ClampedArray.from({length: 4096}, (_, i) => (i * 73 + (i >> 2)) % 256);
  const original = pixels.slice();
  for (const color of [[0, 0, 0], [1, 1, 1], [.5, .1, .9], [1/255, 128/255, 254/255]]) {
    const expected = pixels.slice();
    for (let i = 0; i < expected.length; i++) if (i % 4 !== 3) expected[i] *= color[i % 4];
    assert.deepEqual(tintPixels(pixels, color), expected);
    assert.deepEqual(pixels, original);
  }
});
