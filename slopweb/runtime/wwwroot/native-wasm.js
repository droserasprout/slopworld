// The AOT module can exceed a browser's HTTP cache item limit. Keep one
// SRI-verified native image per origin, even when tabs run different builds.
import {browserCaches, cacheWhileReading} from './browser-storage.js';

const slot = '/_framework/dotnet.native.wasm';
const maxBytes = 128 * 1024 * 1024;

async function loadNativeWasm(url, integrity, storage) {
  let cache;
  try {
    cache = await storage.open('slopweb-native-wasm-v1');
    const saved = await cache.match(slot);
    if (saved?.headers.get('x-slop-integrity') === integrity) {
      const blob = await saved.blob();
      if (blob.size && blob.size <= maxBytes) {
        // CacheStorage.match doesn't perform Fetch's subresource integrity check.
        const digest = await crypto.subtle.digest('SHA-256', await blob.arrayBuffer());
        if ('sha256-' + btoa(String.fromCharCode(...new Uint8Array(digest))) === integrity)
          return new Response(blob, {headers:{'Content-Type':'application/wasm'}});
      }
    }
  } catch { /* Denied storage or an unreadable entry falls through to the SDK fetch policy. */ }

  const response = await fetch(url, {cache:'force-cache', integrity});
  const length = Number(response.headers.get('x-slop-decoded-length'));
  if (!cache || response.status !== 200 || response.headers.get('content-type') !== 'application/wasm'
      || /\bno-store\b/i.test(response.headers.get('cache-control') ?? '')
      || !Number.isSafeInteger(length) || length <= 0 || length > maxBytes)
    return response;

  const headers = {'Content-Type':'application/wasm', 'Content-Length':String(length)};
  const decoded = new Response(response.body, {headers});
  return cacheWhileReading(decoded, body => {
    let count = 0;
    const checked = body.pipeThrough(new TransformStream({
      transform(chunk, controller) {
        count += chunk.byteLength;
        if (count > length) throw Error('Native WASM exceeds its declared size');
        controller.enqueue(chunk);
      },
      flush() {
        if (count !== length) throw Error('Incomplete native WASM cache entry');
      },
    }));
    return cache.put(slot, new Response(checked, {headers:{...headers, 'x-slop-integrity':integrity}}));
  }).response;
}

// The SDK requires a synchronous fall-through for module imports and all other
// resources. Only replace the fetch for the SDK's fingerprinted native WASM.
export function nativeWasmResource(type, name, url, integrity) {
  if (type !== 'dotnetwasm' || !/^dotnet\.native\.[a-z0-9]{10}\.wasm$/.test(name)
      || !/^sha256-[A-Za-z0-9+/]{43}=$/.test(integrity)) return undefined;
  const target = new URL(url);
  if (target.origin !== location.origin || !target.pathname.endsWith('/_framework/' + name)) return undefined;
  const storage = browserCaches();
  if (storage) return loadNativeWasm(url, integrity, storage);
}
