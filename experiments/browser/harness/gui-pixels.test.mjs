import test from 'node:test';
import assert from 'node:assert/strict';
import { decodeGuiPixels } from '../runtime/wwwroot/gui-pixels.js';

test('RGBA decode preserves every byte, empty payloads and invalid-input failures', () => {
  const original = Uint8Array.fromBase64;
  const bytes = Uint8ClampedArray.from({length: 1024}, (_, i) => i % 256);
  try {
    for (const decoder of [original, undefined]) {
      Uint8Array.fromBase64 = decoder;
      assert.deepEqual(decodeGuiPixels(Buffer.from(bytes).toString('base64')), bytes);
      assert.deepEqual(decodeGuiPixels(''), new Uint8ClampedArray());
      assert.throws(() => decodeGuiPixels('!invalid!'));
    }
  } finally { Uint8Array.fromBase64 = original; }
});

test('native decoder shares only its returned byte view without copying', () => {
  const original = Uint8Array.fromBase64, bytes = new Uint8Array([99, 0, 128, 255, 64, 99]);
  try {
    Uint8Array.fromBase64 = () => bytes.subarray(1, 5);
    const pixels = decodeGuiPixels('fixture');
    assert.deepEqual(pixels, new Uint8ClampedArray([0, 128, 255, 64]));
    assert.equal(pixels.buffer, bytes.buffer);
    assert.equal(pixels.byteOffset, 1);
  } finally { Uint8Array.fromBase64 = original; }
});
