import assert from 'node:assert/strict';
import test from 'node:test';
import {localRasterizer} from '../../runtime/wwwroot/workspace-rasterizer.js';

function fixture(t) {
  const memory = new ArrayBuffer(256);
  const wasm = {
    HEAPU8: new Uint8Array(memory),
    HEAP32: new Int32Array(memory),
    HEAPF32: new Float32Array(memory),
    _malloc: t.mock.fn(() => 128),
    _free: t.mock.fn(),
    _font_open: t.mock.fn(() => 31),
    _font_close: t.mock.fn(),
    _font_shutdown: t.mock.fn(),
    _font_result: () => 16,
    _font_size: t.mock.fn(() => 1),
    _font_glyph: t.mock.fn(() => 1),
  };
  const rasterizer = localRasterizer(wasm);
  const call = request => JSON.parse(rasterizer.call(JSON.stringify({hash: 'face', ...request})));
  return {wasm, rasterizer, call};
}

test('failed font installs release owned memory and can retry the same face', t => {
  const {wasm, call} = fixture(t);
  wasm._malloc.mock.mockImplementationOnce(() => 0);
  wasm._font_open.mock.mockImplementationOnce(() => 0);
  const font = {font: 'AQID'};
  assert.throws(() => call(font), /allocation failed/);
  assert.equal(wasm._font_open.mock.callCount(), 0);
  assert.throws(() => call(font), /Invalid outline font/);
  assert.deepEqual(wasm._free.mock.calls.map(c => c.arguments), [[128]]);
  assert.equal(call({registered: true}), false);

  call(font);
  call(font);
  assert.equal(call({registered: true}), true);
  assert.deepEqual(wasm.HEAPU8.slice(128, 131), Uint8Array.of(1, 2, 3));
  assert.equal(wasm._font_open.mock.callCount(), 2);
  call({close: true});
  assert.deepEqual(wasm._font_close.mock.calls.map(c => c.arguments), [[31]]);
  assert.equal(wasm._font_shutdown.mock.callCount(), 1);
  assert.equal(call({registered: true}), false);
});

test('glyphs preserve fractional metrics and skip padding between bitmap rows', t => {
  const {wasm, rasterizer, call} = fixture(t);
  rasterizer.install('face', Uint8Array.of(7));
  const size = {size: 13.5, density: 2, flags: 513};
  wasm.HEAPF32.set([4.5, -1, 2, 10.5, -3.5], 4);
  wasm.HEAP32.set([2, 2, 3, 100], 9);
  wasm.HEAPU8.set([1, 2, 99, 3, 4, 99], 100);
  assert.deepEqual(call(size), {ascent: 10.5, descent: -3.5});
  const glyph = call({...size, scalar: 937});
  assert.deepEqual(wasm._font_glyph.mock.calls[0].arguments, [31, 937, 13.5, 2, 513]);
  assert.deepEqual(glyph, {
    advance: 4.5, x: -1, y: 2, ascent: 10.5, descent: -3.5,
    width: 2, height: 2, pixels: 'AQIDBA==',
  });
});

test('unknown faces, invalid sizes and native errors stay distinct from missing glyphs', t => {
  const {wasm, rasterizer, call} = fixture(t);
  assert.throws(() => call({size: 12}), /Font is not loaded/);
  rasterizer.install('face', Uint8Array.of(7));
  wasm._font_size.mock.mockImplementationOnce(() => -1);
  assert.throws(() => call({size: 0}), /Invalid font size/);
  wasm._font_glyph.mock.mockImplementationOnce(() => -1);
  assert.throws(() => call({scalar: 65}), /rasterization failed/);
  wasm._font_glyph.mock.mockImplementationOnce(() => 0);
  assert.deepEqual(call({scalar: 65}), {missing: true});
});

test('the face limit permits reuse at capacity and admits new faces after close', t => {
  const {wasm, rasterizer} = fixture(t);
  const bytes = Uint8Array.of(1);
  for (let i = 0; i < 128; i++) rasterizer.install(String(i), bytes);
  rasterizer.install('0', bytes);
  assert.throws(() => rasterizer.install('next', bytes), /source limit/);
  assert.equal(wasm._malloc.mock.callCount(), 128);
  rasterizer.close();
  rasterizer.install('next', bytes);
  assert.equal(wasm._malloc.mock.callCount(), 129);
});
