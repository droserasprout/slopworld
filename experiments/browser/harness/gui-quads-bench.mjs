// Synthetic transport-only measurement, excluding WASM, Canvas and workspace latency.
import {GuiQuadCache} from '../../../slopweb/runtime/wwwroot/gui-quads.js';
import assert from 'node:assert/strict';

const packets = Array.from({length:32}, (_, frame) => {
  const count = 128, length = 512 * 8;
  const bytes = Buffer.alloc(8 + count * 8 + length * 2);
  bytes.writeUInt32LE(0x31514c53, 0); bytes.writeUInt32LE(count, 4);
  for (let i = 0; i < count; i++) bytes.writeDoubleLE((i + frame) * .03125, 8 + i * 8);
  for (let i = 0; i < length; i++) bytes.writeUInt16LE(i % count, 8 + count * 8 + i * 2);
  return bytes.toString('base64');
});
const caches = [new GuiQuadCache(0), new GuiQuadCache(), new GuiQuadCache(1)];
const samples = [[], [], []], iterations = 2048;
let checksum = 0;
for (const cache of caches) for (const packet of packets) cache.decode(packet);
for (let round = 0; round < 10; round++) {
  for (const mode of round % 2 ? [2, 1, 0] : [0, 1, 2]) {
    // Match newly parsed packets instead of repeatedly looking up the same JS
    // string objects. Parsing itself is outside this decode-only measurement.
    const inputs = JSON.parse(JSON.stringify(Array.from({length:iterations}, (_, i) => packets[i % packets.length])));
    const start = performance.now();
    for (const packet of inputs) checksum += caches[mode].decode(packet)[0];
    samples[mode].push((performance.now() - start) / iterations);
  }
}
assert.equal(caches[1].size, packets.length);
assert.equal(caches[0].size, 0);
assert.equal(caches[2].size, 1);
const median = values => {
  const sorted = values.toSorted((a, b) => a - b);
  return (sorted[4] + sorted[5]) / 2;
};
console.log(JSON.stringify({
  measurement:'Node synthetic repeated packed-quad decode; excludes WASM, Canvas and workspace latency',
  node:process.version, nativeBase64:typeof Uint8Array.fromBase64 === 'function',
  quadsPerBatch:512, uniqueBatches:packets.length, iterationsPerRound:iterations, rounds:10,
  medianMsPerBatch:{uncached:median(samples[0]), cached:median(samples[1]), cacheChurn:median(samples[2])},
  uncachedMsPerBatch:samples[0], cachedMsPerBatch:samples[1], cacheChurnMsPerBatch:samples[2],
  retainedBytes:caches[1].retainedBytes, checksum,
}, null, 2));
