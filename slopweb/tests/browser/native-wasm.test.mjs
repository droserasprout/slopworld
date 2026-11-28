// Exercise the SDK resource callback with real WASM compilation and Fetch SRI.
import test from 'node:test';
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {nativeWasmResource} from '../../runtime/wwwroot/native-wasm.js';

const origin = 'https://workspace.test';
const file = 'dotnet.native.ve45dme62l.wasm';
const url = origin + '/workspace/_framework/' + file;
const wasm = Buffer.from([0, 97, 115, 109, 1, 0, 0, 0]);
const hash = bytes => 'sha256-' + createHash('sha256').update(bytes).digest('base64');

function fixture(t) {
  const fetchBytes = globalThis.fetch;
  const entries = new Map();
  const calls = [];
  let write;
  const cache = {
    async match(key) { return entries.get(key)?.clone(); },
    put(key, response) {
      write = response.arrayBuffer().then(data => {
        entries.set(key, new Response(data, {headers:response.headers}));
      });
      return write;
    },
  };
  const state = {bytes:wasm, length:wasm.length};
  const storage = {open:async () => cache};
  for (const [name, value] of Object.entries({location:{origin}, caches:storage})) {
    const original = Object.getOwnPropertyDescriptor(globalThis, name);
    Object.defineProperty(globalThis, name, {value, configurable:true});
    t.after(() => {
      if (original) Object.defineProperty(globalThis, name, original);
      else delete globalThis[name];
    });
  }
  t.mock.method(globalThis, 'fetch', async (url, options) => {
    calls.push({url, options});
    const verified = await fetchBytes('data:application/wasm;base64,' + state.bytes.toString('base64'), options);
    return new Response(verified.body, {headers:{
      'content-type':'application/wasm', 'content-encoding':'gzip',
      'content-length':'3', 'x-slop-decoded-length':String(state.length),
    }});
  });
  async function compile(name=file, integrity=hash(state.bytes)) {
    write = undefined;
    const response = await nativeWasmResource('dotnetwasm', name, url.replace(file, name), integrity);
    await WebAssembly.compileStreaming(response);
    // Cache writes can settle after compilation, including rejected writes for invalid lengths.
    await Promise.allSettled([write]);
  }
  return {entries, calls, state, cache, storage, compile};
}

test('native WASM compiles warm without a fetch and replaces the single slot across SDK builds', async t => {
  const {compile, calls, entries, state} = fixture(t);
  await compile();
  assert.equal(entries.size, 1);
  const [saved] = entries.values();
  assert.equal(saved.headers.get('content-encoding'), null);
  assert.equal(saved.headers.get('content-length'), String(wasm.length));
  await compile();
  assert.deepEqual(calls, [{url, options:{cache:'force-cache', integrity:hash(wasm)}}]);

  // An empty custom section produces another valid native module.
  state.bytes = Buffer.concat([wasm, Buffer.from([0, 1, 0])]);
  state.length = state.bytes.length;
  const next = 'dotnet.native.0123456789.wasm';
  await compile(next);
  await compile(next);
  assert.equal(entries.size, 1);
  assert.equal(calls.length, 2);
  assert.equal(calls[1].options.integrity, hash(state.bytes));

  // A cache entry with the right metadata but wrong bytes must never execute.
  const [key] = entries.keys();
  entries.set(key, new Response(wasm, {headers:entries.get(key).headers}));
  await compile(next);
  await compile(next);
  assert.equal(calls.length, 3);
});

test('storage failures, oversize declarations and length mismatches preserve native startup', async t => {
  const {compile, entries, state, storage, cache} = fixture(t);
  for (const length of [0, wasm.length - 1, wasm.length + 1, 128 * 1024 * 1024 + 1]) {
    state.length = length;
    await compile();
    assert.equal(entries.size, 0);
  }
  state.length = wasm.length;
  cache.put = async () => { throw Error('Quota exceeded'); };
  await compile();
  storage.open = async () => { throw Error('Denied'); };
  await compile();
  await assert.rejects(compile(file, hash(Buffer.from('other bytes'))));
  assert.equal(entries.size, 0);
});

test('SDK owns imports, mutable names, other origins, and requests without usable integrity or storage', t => {
  fixture(t);
  for (const args of [
    ['dotnetjs', file, url, hash(wasm)],
    ['assembly', file, url, hash(wasm)],
    ['dotnetwasm', 'dotnet.native.wasm', url, hash(wasm)],
    ['dotnetwasm', file, url, ''],
    ['dotnetwasm', file, url.replace('_framework', 'game'), hash(wasm)],
    ['dotnetwasm', file, url.replace(origin, 'https://another.test'), hash(wasm)],
  ]) assert.equal(nativeWasmResource(...args), undefined);
  Object.defineProperty(globalThis, 'caches', {configurable:true, get(){throw Error('Denied');}});
  assert.equal(nativeWasmResource('dotnetwasm', file, url, hash(wasm)), undefined);
});
