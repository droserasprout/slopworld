(() => ({
  runtime: window.__workspace?.status,
  frames: window.__workspace?.frames,
  loading: !!document.getElementById('loading-screen'),
  canvases: document.querySelectorAll('canvas').length,
  visibility: document.visibilityState,
  error: window.__probeResult?.error,
  resources: performance.getEntriesByType('resource').filter(entry=>
    /loading|LoadingFont/.test(entry.name)).map(entry=>({name:new URL(entry.name).pathname,duration:entry.duration})),
}))()
