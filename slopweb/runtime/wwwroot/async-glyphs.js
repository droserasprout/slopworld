// The synchronous managed callback observes completed values only. Pending
// glyphs are retried after one coalesced native invalidation, never marked absent.
export function asyncGlyphs(local,worker,wake) {
  const replies=new Map(), pending=new Map(), requests=new Map();
  let next=0,closed=false,failed=false,timer,dirty=false,settling=0;
  function send(message) {
    if (closed || failed) return Promise.reject(Error('Glyph worker unavailable'));
    return new Promise((resolve,reject)=>{
      const id=++next; requests.set(id,{resolve,reject});
      try { worker.postMessage({...message,id}); }
      catch(error) { requests.delete(id);reject(error); }
    });
  }
  const flush=()=>{
    clearTimeout(timer);timer=undefined;
    if(closed || !dirty)return;
    dirty=false;wake();
  };
  const notify=()=>{
    if(closed)return;
    dirty=true;
    if(!settling && timer===undefined)timer=setTimeout(flush,16);
  };
  worker.onmessage=({data})=>{
    const request=requests.get(data.id);
    if (!request || closed) return;
    requests.delete(data.id);
    if (data.error) request.reject(Error(data.error)); else request.resolve(data.value);
  };
  worker.onerror=()=>{
    failed=true;
    for(const request of requests.values()) request.reject(Error('Glyph worker failed'));
    requests.clear();
  };
  async function install(hash,bytes) {
    await send({hash,font:bytes});
    if (!closed) local.install(hash,bytes);
  }
  function call(json) {
    const request=JSON.parse(json);
    if (request.close) {close();return '{}';}
    if (request.scalar===undefined || request.scalar<128) return local.call(json);
    const key=JSON.stringify([request.hash,request.scalar,request.size,request.density,request.flags]);
    const reply=replies.get(key);
    if (reply!==undefined) {
      if (reply instanceof Error) {
        // The managed face records failure until its explicit retry. Deliver
        // this error once so that retry can submit fresh work to the worker.
        replies.delete(key);
        throw reply;
      }
      return reply;
    }
    if (!pending.has(key) && pending.size<256) {
      const work=send({request}).then(value=>{
        if (!closed) replies.set(key,value);
      },error=>{if (!closed) replies.set(key,error);}).finally(()=>{
        pending.delete(key);
        // Native atlas ownership is independently bounded. Evicted JS replies
        // can be read from persistent storage if a new variant needs them.
        while(replies.size>512) replies.delete(replies.keys().next().value);
        notify();
      });
      pending.set(key,work);
    }
    return '{"pending":true}';
  }
  function close() {
    if(closed)return;
    closed=true;dirty=false;clearTimeout(timer);worker.terminate();local.close();
    for(const request of requests.values())request.reject(Error('Glyph worker closed'));
    requests.clear();replies.clear();pending.clear();
  }
  async function settle() {
    // Startup owns the next draw: defer intermediate global invalidations, then
    // publish once before returning so it cannot draw against stale text caches.
    const changed=dirty || pending.size>0;
    settling++;clearTimeout(timer);timer=undefined;
    try {await Promise.all([...pending.values()]);}
    finally {if(--settling===0)flush();}
    return !closed && changed;
  }
  return {call,install,close,settle};
}
