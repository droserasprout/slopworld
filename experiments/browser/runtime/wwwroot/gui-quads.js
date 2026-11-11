// SLQ1 batches contain a table of original JSON-parsed doubles and uint16 indices.
// Keep numeric arrays for small batches and existing diagnostic producers.
const littleEndian = new Uint8Array(new Uint16Array([1]).buffer)[0] === 1;

function unpackGuiQuads(value) {
  if (typeof value !== 'string') return value;
  const bytes = typeof Uint8Array.fromBase64 === 'function'
    ? Uint8Array.fromBase64(value)
    : Uint8Array.from(atob(value), c => c.charCodeAt(0));
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  if (bytes.length < 8 || view.getUint32(0, true) !== 0x31514c53)
    throw new Error('Invalid packed GUI quad header');
  const count = view.getUint32(4, true), tableEnd = 8 + count * 8;
  if (!count || count > 65535 || tableEnd > bytes.length || (bytes.length - tableEnd) % 16)
    throw new Error('Invalid packed GUI quad length');
  const table = littleEndian && bytes.byteOffset % 8 === 0
    ? new Float64Array(bytes.buffer, bytes.byteOffset + 8, count)
    : Float64Array.from({length: count}, (_, i) => view.getFloat64(8 + i * 8, true));
  const length = (bytes.length - tableEnd) / 2;
  const indices = littleEndian && bytes.byteOffset % 2 === 0
    ? new Uint16Array(bytes.buffer, bytes.byteOffset + tableEnd, length)
    : Uint16Array.from({length}, (_, i) => view.getUint16(tableEnd + i * 2, true));
  const quads = new Float64Array(length);
  for (let i = 0; i < length; i++) {
    if (indices[i] >= count) throw new Error('Invalid packed GUI quad index');
    quads[i] = table[indices[i]];
  }
  return quads;
}

// Geometry is immutable after decoding. Native packets often retain the same
// glyph batches while other commands change; keep this cache independent of
// texture identity, tint and Canvas resource lifetime. Callers must only read
// the returned arrays. Bound both entry count and encoded + decoded storage.
export class GuiQuadCache {
  #entries = new Map();
  #bytes = 0;
  constructor(entryLimit = 512, byteLimit = 8 * 1024 * 1024) {
    this.entryLimit = entryLimit;
    this.byteLimit = byteLimit;
  }
  get size() { return this.#entries.size; }
  get retainedBytes() { return this.#bytes; }
  decode(value) {
    if (typeof value !== 'string') return value;
    const previous = this.#entries.get(value);
    if (previous) {
      this.#entries.delete(value);
      this.#entries.set(value, previous);
      return previous.quads;
    }
    const quads = unpackGuiQuads(value);
    const bytes = value.length * 2 + quads.byteLength;
    if (this.entryLimit <= 0 || bytes > this.byteLimit) return quads;
    while (this.#entries.size >= this.entryLimit || this.#bytes + bytes > this.byteLimit) {
      const oldest = this.#entries.keys().next().value;
      this.#bytes -= this.#entries.get(oldest).bytes;
      this.#entries.delete(oldest);
    }
    this.#entries.set(value, {quads, bytes});
    this.#bytes += bytes;
    return quads;
  }
}

const quadCache = new GuiQuadCache();
export function decodeGuiQuads(value) { return quadCache.decode(value); }
