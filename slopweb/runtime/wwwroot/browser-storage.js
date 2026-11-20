// Browser storage policy can deny access at the global getter itself.
export function browserCaches() {
  try { return globalThis.caches; }
  catch { return undefined; }
}

// Let the runtime drive the transfer. Each chunk also waits for the cache
// reader, so neither consumer can accumulate a whole-file queue.
export function cacheWhileReading(response, store) {
  const reader = response.body.getReader();
  let cacheController;
  const copy = new TransformStream({start(controller) { cacheController = controller; }});
  const writer = copy.writable.getWriter();
  let caching = true;
  const stored = Promise.resolve().then(() => store(copy.readable)).catch(error => {
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
