// Cache the large archive explicitly: HTTP caches may reject individual large
// entries. Revalidate its identity before reuse; publish only complete bodies.
import {browserCaches, cacheWhileReading} from './browser-storage.js';

const forbidsStorage = response => response.headers.get('cache-control')?.split(',')
  .some(directive => directive.trim().toLowerCase() === 'no-store');

export async function openAssetArchive(url, request=globalThis.fetch, storage=browserCaches()) {
  let cache, metadata;
  const evict=async()=>{
    try {await cache?.delete(url);} catch { /* Storage access is optional. */ }
  };
  try {
    cache=await storage?.open('slopweb-asset-archive-v1');
    if(cache) metadata=await request(url,{method:'HEAD',cache:'no-store'});
  } catch { /* Fetch the body directly when revalidation is unavailable. */ }
  const etag=metadata?.headers.get('etag');
  const expected=Number(metadata?.headers.get('x-slop-decoded-length'));
  const noStore=metadata && forbidsStorage(metadata);
  const cacheable=metadata?.ok && !noStore && etag && Number.isSafeInteger(expected) && expected>0;
  const matchesMetadata=response=>response?.headers.get('etag')===etag
    && Number(response.headers.get('x-slop-decoded-length'))===expected;
  const validate=response=>{
    let bytes=0;
    const body=response.body.pipeThrough(new TransformStream({
      transform(chunk,controller) {bytes+=chunk.byteLength;controller.enqueue(chunk);},
      async flush() {
        if(bytes!==expected) {
          await evict();
          throw Error(`Incomplete cached asset transfer: ${bytes}/${expected}`);
        }
      },
    }));
    // Fetch streams are already decoded; cached responses contain those bytes.
    const headers=new Headers(response.headers);
    headers.delete('content-encoding');headers.set('content-length',String(expected));
    return new Response(body,{status:response.status,headers});
  };
  if(noStore) await evict();
  else if(cacheable) {
    try {
      const saved=await cache.match(url);
      if(saved && forbidsStorage(saved)) await evict();
      else if(matchesMetadata(saved))
        return {response:validate(saved),stored:Promise.resolve()};
    } catch { /* A failed lookup must not prevent a fresh transfer. */ }
  }
  const response=await request(url,{cache:'no-store'});
  // A deployment between HEAD and GET must not bind the new body to old metadata.
  if(forbidsStorage(response)) await evict();
  else if(cacheable && response.ok && matchesMetadata(response)) {
    const decoded=validate(response);
    return cacheWhileReading(decoded, body => cache.put(url, new Response(body, {
      headers:decoded.headers, status:decoded.status,
    })));
  }
  return {response,stored:Promise.resolve()};
}
