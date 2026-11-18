// Browser storage policy can deny access at the global getter itself.
export function browserCaches() {
  try { return globalThis.caches; }
  catch { return undefined; }
}
