import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { decodeGuiQuads, GuiQuadCache } from '../runtime/wwwroot/gui-quads.js';
import { renderGui } from '../runtime/wwwroot/gui.js';

const pack = values => {
  const bytes = new Uint8Array(8 + values.length * 10), view = new DataView(bytes.buffer);
  view.setUint32(0, 0x31514c53, true); view.setUint32(4, values.length, true);
  values.forEach((v, i) => {
    view.setFloat64(8 + i * 8, v, true);
    view.setUint16(8 + values.length * 8 + i * 2, i, true);
  });
  return Buffer.from(bytes).toString('base64');
};

test('packed quads preserve decimal JS doubles, signed zero, and order', () => {
  const values = [.1, -.1, -0, 0, 1e-45, 3.4028235e38, .03125, 720.1234];
  assert.deepEqual(Array.from(decodeGuiQuads(pack(values))), values);
  assert.equal(decodeGuiQuads(values), values);
  assert.equal(decodeGuiQuads([]).length, 0);
  assert.throws(() => decodeGuiQuads('AAAAAA=='), /quad header/);
});

test('managed packed packet decodes to the exact original numeric JSON values', {skip: !process.env.GUI_QUAD_FIXTURE}, () => {
  const fixture = JSON.parse(readFileSync(process.env.GUI_QUAD_FIXTURE, 'utf8'));
  assert.deepEqual(Array.from(decodeGuiQuads(fixture.packed[0].value.quads)), fixture.numeric[0].value.quads);
});

test('native base64 path handles unaligned buffers and rejects invalid tables', () => {
  const decodeGuiQuads = value => new GuiQuadCache(0).decode(value);
  const original = Uint8Array.fromBase64;
  const values = [.1, -.1, -0, 0, 1e-45, 3.4028235e38, .03125, 720.1234];
  try {
    Uint8Array.fromBase64 = value => {
      const bytes = Buffer.from(value, 'base64'), unaligned = new Uint8Array(bytes.length + 1);
      unaligned.set(bytes, 1); return unaligned.subarray(1);
    };
    assert.deepEqual(Array.from(decodeGuiQuads(pack(values))), values);
    const bad = Buffer.from(pack(values), 'base64');
    bad.writeUInt32LE(65536, 4);
    assert.throws(() => decodeGuiQuads(bad.toString('base64')), /quad length/);
    bad.writeUInt32LE(values.length, 4);
    bad.writeUInt16LE(65535, 8 + values.length * 8);
    assert.throws(() => decodeGuiQuads(bad.toString('base64')), /quad index/);
  } finally { Uint8Array.fromBase64 = original; }
});

test('decoded geometry reuses exact packets and preserves signed zero and changed coordinates', () => {
  const cache = new GuiQuadCache();
  const values = [-0, 0, .1, -.1, 3, 4, 5, 6], packet = pack(values);
  const first = cache.decode(packet);
  assert.equal(cache.decode((' ' + packet).slice(1)), first);
  assert.deepEqual(Array.from(first), values);
  assert.notEqual(cache.decode(pack([0, ...values.slice(1)])), first);
  assert.equal(cache.decode(values), values);
  assert.equal(cache.size, 2);
});

test('decoded geometry evicts least recently used entries and bounds encoded and decoded bytes', () => {
  const cache = new GuiQuadCache(2);
  const packets = [1, 2, 3].map(x => pack(Array(8).fill(x)));
  const first = cache.decode(packets[0]), second = cache.decode(packets[1]);
  assert.equal(cache.decode(packets[0]), first);
  cache.decode(packets[2]);
  assert.equal(cache.size, 2);
  assert.equal(cache.decode(packets[0]), first);
  assert.notEqual(cache.decode(packets[1]), second);
  const bytes = packets[0].length * 2 + first.byteLength;
  const bounded = new GuiQuadCache(100, bytes);
  bounded.decode(packets[0]); bounded.decode(packets[1]);
  assert.equal(bounded.size, 1);
  assert.equal(bounded.retainedBytes, bytes);
  bounded.decode(pack(Array(16).fill(1)));
  assert.equal(bounded.size, 1);
  assert.equal(bounded.retainedBytes, bytes);
  const disabled = new GuiQuadCache(0);
  assert.notEqual(disabled.decode(packets[0]), disabled.decode(packets[0]));
  assert.equal(disabled.retainedBytes, 0);
});

test('invalid geometry is rejected repeatedly without evicting retained valid geometry', () => {
  const cache = new GuiQuadCache(1), packet = pack(Array(8).fill(1));
  const first = cache.decode(packet);
  for (let i = 0; i < 2; i++) assert.throws(() => cache.decode('AAAAAA=='), /quad header/);
  assert.equal(cache.size, 1);
  assert.equal(cache.decode(packet), first);
});

test('packed and numeric batches issue identical ordered Canvas calls across clips and targets', () => {
  const previousDocument = globalThis.document;
  const calls = [];
  const context = () => new Proxy({}, {get: (_, method) => (...args) => {
    if (method === 'getTransform') return {a:1,b:0,c:0,d:1,e:0,f:0};
    if (method === 'getContextAttributes') return {colorType:'unorm8'};
    calls.push([method, ...args.map(v => typeof v === 'object' ? 'source' : v)]);
  }});
  const canvas = () => ({width:80,height:60,getContext:() => context()});
  globalThis.document = {createElement:canvas};
  const values = [2.1,3.2,19,21,0,0,1,1, 8,9,22,17,.5,0,.5,1, -3,15,14,11,0,.5,1,.5];
  const commands = quads => [
    {kind:'image',value:{id:42,width:2,height:2,pixels:''}},
    {kind:'matrix',value:[1.5,0,0,1.5,2,3]},
    {kind:'clipScreen',value:[5,4,45,34]},
    {kind:'target',value:{id:43,width:80,height:60}},
    {kind:'imageBatch',value:{id:42,color:[1,1,1,.6],quads}},
    {kind:'target',value:{id:0,width:0,height:0}},
    {kind:'pop',value:0},
    {kind:'releaseImage',value:42},
    {kind:'releaseTarget',value:43},
  ];
  const previousImageData = globalThis.ImageData;
  const previousDOMPoint = globalThis.DOMPoint;
  globalThis.ImageData = class {};
  globalThis.DOMPoint = class {
    constructor(x, y) { this.x = x; this.y = y; }
    matrixTransform(m) { return {x:m.a*this.x+m.c*this.y+m.e, y:m.b*this.x+m.d*this.y+m.f}; }
  };
  try {
    renderGui(canvas(), commands(values));
    const expected = calls.splice(0);
    const packed = commands(pack(values));
    renderGui(canvas(), packed);
    assert.deepEqual(calls, expected);
    assert.deepEqual(Array.from(packed[4].value.quads), values);
    // A fresh frame, upload and render target must still replay all Canvas calls
    // when geometry comes from the cross-frame cache.
    calls.length = 0;
    renderGui(canvas(), commands(pack(values)));
    assert.deepEqual(calls, expected);
  } finally {
    globalThis.document = previousDocument;
    globalThis.ImageData = previousImageData;
    globalThis.DOMPoint = previousDOMPoint;
  }
});
