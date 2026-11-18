// Cache the large archive explicitly: HTTP caches may reject individual large
// entries. Revalidate its identity before reuse; publish only complete bodies.
import {browserCaches} from './browser-storage.js';

// Let managed reads drive the transfer. Each chunk also waits for the cache
// reader, so neither consumer can accumulate an archive-sized queue.
function cacheWhileReading(cache, url, response) {
  const reader = response.body.getReader();
  let cacheController;
  const copy = new TransformStream({start(controller) { cacheController = controller; }});
  const writer = copy.writable.getWriter();
  let caching = true;
  const stored = Promise.resolve().then(() =>
    cache.put(url, new Response(copy.readable, {headers:response.headers, status:response.status}))
  ).catch(error => {
    caching = false;
    cacheController.error(error); // Unblock a write even if storage never started reading.
  });
  const body = new ReadableStream({
    async pull(controller) {
      try {
        const {done, value} = await reader.read();
        if (done) {
          if (caching) await writer.close().catch(() => {});
          controller.close();
        } else {
          if (caching) await writer.write(value).catch(() => { caching = false; });
          controller.enqueue(value);
        }
      } catch (error) {
        cacheController.error(error);
        throw error;
      }
    },
    cancel(reason) {
      cacheController.error(reason);
      return reader.cancel(reason);
    },
  }, {highWaterMark:0});
  return {response:new Response(body, {headers:response.headers,status:response.status}), stored};
}

export async function openAssetArchive(url, request=globalThis.fetch, storage=browserCaches()) {
  const uncached=async()=>({response:await request(url),stored:Promise.resolve()});
  let cache, metadata;
  try {
    if(!storage)return uncached();
    cache=await storage.open('slopweb-asset-archive-v1');
    metadata=await request(url,{method:'HEAD',cache:'no-store'});
  } catch {return uncached();}
  const etag=metadata.headers.get('etag');
  const expected=Number(metadata.headers.get('x-slop-decoded-length'));
  if(!metadata.ok || !etag || !Number.isSafeInteger(expected) || expected<=0)return uncached();
  const validate=response=>{
    let bytes=0;
    const body=response.body.pipeThrough(new TransformStream({
      transform(chunk,controller) {bytes+=chunk.byteLength;controller.enqueue(chunk);},
      async flush() {
        if(bytes!==expected) {
          try {await cache.delete(url);} catch { /* Cache storage is optional. */ }
          throw Error(`Incomplete cached asset transfer: ${bytes}/${expected}`);
        }
      },
    }));
    // Fetch streams are already decoded; cached responses contain those bytes.
    const headers=new Headers(response.headers);
    headers.delete('content-encoding');headers.set('content-length',String(expected));
    return new Response(body,{status:response.status,headers});
  };
  try {
    const saved=await cache.match(url);
    if(saved?.headers.get('etag')===etag && Number(saved.headers.get('x-slop-decoded-length'))===expected)
      return {response:validate(saved),stored:Promise.resolve()};
  } catch { /* A failed lookup must not prevent a fresh transfer. */ }
  const response=await request(url);
  // A deployment between HEAD and GET must not bind the new body to old metadata.
  if(!response.ok || response.headers.get('etag')!==etag || Number(response.headers.get('x-slop-decoded-length'))!==expected)
    return {response,stored:Promise.resolve()};
  return cacheWhileReading(cache,url,validate(response));
}
