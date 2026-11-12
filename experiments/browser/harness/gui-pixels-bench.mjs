// Synthetic upload decoding only; excludes Canvas, WASM and workspace latency.
import assert from 'node:assert/strict';

import { decodeGuiPixels } from '../../../slopweb/runtime/wwwroot/gui-pixels.js';

const nativeDecoder = Uint8Array.fromBase64;
const reference = value => Uint8ClampedArray.from(atob(value), c => c.charCodeAt(0));
const results = [];
let checksum = 0;
try {
  for (const side of [256, 512]) {
    const bytes = Buffer.from(Uint8Array.from({length: side * side * 4}, (_, i) => i % 256));
    const encoded = bytes.toString('base64'), iterations = 8, rounds = 7;
    const samples = [[], [], []];
    for (let round = 0; round < rounds; round++) {
      for (const mode of round % 2 ? [2, 1, 0] : [0, 1, 2]) {
        Uint8Array.fromBase64 = mode === 2 ? undefined : nativeDecoder;
        const decode = mode === 0 ? reference : decodeGuiPixels;
        assert.deepEqual(Buffer.from(decode(encoded)), bytes);
        const start = performance.now();
        for (let i = 0; i < iterations; i++) checksum += decode(encoded)[127];
        samples[mode].push((performance.now() - start) / iterations);
      }
    }
    const median = values => values.toSorted((a, b) => a - b)[3];
    results.push({side, bytes: bytes.length, iterations, rounds,
      medianMsPerUpload: {reference: median(samples[0]), candidate: median(samples[1]), fallback: median(samples[2])},
      samples});
  }
} finally { Uint8Array.fromBase64 = nativeDecoder; }
console.log(JSON.stringify({measurement: 'Node synthetic RGBA upload decode; excludes Canvas, WASM and workspace latency',
  node: process.version, nativeBase64: typeof nativeDecoder === 'function', results, checksum}, null, 2));
