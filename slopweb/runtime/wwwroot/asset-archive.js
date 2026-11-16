// Cache the large archive explicitly: HTTP caches may reject individual large
// entries. Revalidate its identity before reuse; publish only complete bodies.
export async function openAssetArchive(url, request=globalThis.fetch, storage=globalThis.caches) {
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
  const checked=validate(response);
  const stored=cache.put(url,checked.clone()).catch(()=>{});
  return {response:checked,stored};
}
