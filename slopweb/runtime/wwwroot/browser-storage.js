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

// Managed settings use separate versioned envelopes in this origin's storage.
export function settingsSnapshot(view, key, field) {
  const limit = 131072;
  let restored = '{}';
  try {
    const text = view.localStorage.getItem(key);
    if (text && text.length <= limit) {
      const saved = JSON.parse(text), value = saved?.[field];
      if (saved?.version === 1 && value && typeof value === 'object' && !Array.isArray(value))
        restored = JSON.stringify(value);
    }
  } catch { /* Missing or damaged storage keeps the host defaults. */ }
  function persist(json) {
    try {
      if (json.length <= limit)
        view.localStorage.setItem(key, JSON.stringify({version: 1, [field]: JSON.parse(json)}));
    } catch { /* Quota or denial must not interrupt native controls. */ }
  }
  return [restored, persist];
}
