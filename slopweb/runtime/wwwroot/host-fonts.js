// Host coverage describes installed font files; the browser selects fallback faces
// locally and persists verified immutable files independently of application builds.
export function covers(ranges, scalar) {
  let low=0, high=ranges.length;
  while (low<high) { const middle=(low+high)>>>1; if (ranges[middle][1]<scalar) low=middle+1; else high=middle; }
  return low<ranges.length && ranges[low][0]<=scalar;
}
const hashBytes=async bytes=>Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',bytes)),c=>c.toString(16).padStart(2,'0')).join('');

export class HostFonts {
  constructor(probe, request=globalThis.fetch, storage=globalThis.caches) {
    this.probe=probe;this.request=(...args)=>request(...args);this.storage=storage;
    this.catalogs=new Map();this.files=new Map();this.installed=new Set();this.disposed=false;this.cacheWrite=Promise.resolve();
    this.timings={catalogMs:0,downloadMs:0,installMs:0,networkFiles:0,cachedFiles:0};
  }
  async catalog(family) {
    if (!this.catalogs.has(family)) {
      const pending=(async()=>{
        const started=performance.now();
        const response=await this.request('/host/fonts/catalog?family='+encodeURIComponent(family));
        if (!response.ok) throw Error('Host font catalog unavailable');
        const value=await response.json();
        if (value.version!==1 || !Array.isArray(value.faces) || !Array.isArray(value.order)) throw Error('Unsupported host font catalog');
        const faces=new Map(value.faces.map(face=>{
          if (!/^[a-f0-9]{64}$/.test(face.hash) || !Array.isArray(face.coverage)) throw Error('Invalid host font identity');
          return [face.hash,face];
        }));
        this.timings.catalogMs+=performance.now()-started;
        return {...value,faces};
      })();
      this.catalogs.set(family,pending);
      pending.catch(()=>this.catalogs.delete(family));
    }
    return this.catalogs.get(family);
  }
  async file(hash) {
    if (!/^[a-f0-9]{64}$/.test(hash)) throw Error('Invalid host font hash');
    if (!this.files.has(hash)) {
      const pending=(async()=>{
        const started=performance.now(),url='/host/fonts/files/'+hash;
        let cache;
        try {cache=await this.storage?.open('slopweb.font-files.v1');} catch { /* Storage denial must not prevent text rendering. */ }
        let bytes;
        try {
          const cached=await cache?.match(url);
          if (cached) {
            const candidate=new Uint8Array(await cached.arrayBuffer());
            if (await hashBytes(candidate)===hash) {bytes=candidate;this.timings.cachedFiles++;}
            else await cache.delete(url);
          }
        } catch { /* Fetch a fresh file if cache data cannot be read. */ }
        if (!bytes) {
          const response=await this.request(url);
          if (!response.ok) throw Error('Host font file unavailable');
          bytes=new Uint8Array(await response.arrayBuffer());
          if (bytes.length>64*1024*1024 || await hashBytes(bytes)!==hash) throw Error('Host font file identity mismatch');
          this.timings.networkFiles++;
          try {
            if (cache) await this.persist(cache,url,bytes);
          } catch { /* Quota failure only loses persistent reuse. */ }
        }
        this.timings.downloadMs+=performance.now()-started;
        return bytes;
      })();
      this.files.set(hash,pending);
      // Retain only concurrent downloads. The managed font bank owns installed
      // bytes; keeping a second JavaScript copy would double large CJK faces.
      pending.then(()=>this.files.delete(hash),()=>this.files.delete(hash));
    }
    return this.files.get(hash);
  }
  persist(cache,url,bytes) {
    const write=this.cacheWrite.catch(()=>{}).then(async()=>{
      await cache.put(url,new Response(bytes,{headers:{'Content-Type':'font/ttf','Content-Length':String(bytes.length)}}));
      const keys=await cache.keys();
      let total=0,count=0;
      // Newest insertions survive; bound both file count and storage bytes.
      for (const key of keys.reverse()) {
        const response=await cache.match(key);
        const length=Number(response?.headers?.get('Content-Length'));
        total+=Number.isFinite(length)&&length>0?length:64*1024*1024;
        if (++count>64 || total>128*1024*1024) await cache.delete(key);
      }
    });
    this.cacheWrite=write;return write;
  }
  async install(face) {
    if (this.disposed || this.installed.has(face.hash)) return;
    const bytes=await this.file(face.hash);
    if (this.disposed || this.installed.has(face.hash)) return;
    const started=performance.now();
    this.probe.WorkspaceFont(bytes,JSON.stringify({size:14,loaderFlags:face.loaderFlags,family:face.family}),-1);
    this.installed.add(face.hash);this.timings.installMs+=performance.now()-started;
  }
  async warm(families) {
    for (const family of new Set(families)) {
      const catalog=await this.catalog(family);
      await Promise.all((catalog.preload??[]).map(hash=>{
        const face=catalog.faces.get(hash);
        if (!face) throw Error('Unknown preload font');
        return this.install(face);
      }));
    }
  }
  async resolve(family,scalars) {
    const catalog=await this.catalog(family), assignments={},needed=new Map();
    for (const scalar of scalars) {
      const hash=catalog.order.find(hash=>{const face=catalog.faces.get(hash);return face && covers(face.coverage,scalar);});
      assignments[scalar]=hash?.toUpperCase()??null;
      if (hash) needed.set(hash,catalog.faces.get(hash));
    }
    await Promise.all([...needed.values()].map(face=>this.install(face)));
    return assignments;
  }
  dispose(){this.disposed=true;this.catalogs.clear();this.files.clear();}
}
const clients=new WeakMap();
export function hostFonts(probe,request=globalThis.fetch,storage=globalThis.caches) {
  if (!clients.has(probe)) clients.set(probe,new HostFonts(probe,request,storage));
  return clients.get(probe);
}
