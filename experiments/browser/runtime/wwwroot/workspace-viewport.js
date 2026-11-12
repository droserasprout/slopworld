// Explicit dimensions retain reproducible reference probes; the bookmark uses
// physical browser pixels with CSS-pixel UI geometry on any display density.
export function workspaceParameters(address = globalThis.location) {
  const requested = new URLSearchParams(address.search);
  if (address.pathname !== '/') return requested;
  const parameters = new URLSearchParams('entry&assets&eco&workspace&live&app&referenceAppearance&nativeFonts&responsive&idleYield&packetCache&rowCommands=1');
  if (['width','height','uiScale'].some(key=>requested.has(key))) parameters.delete('responsive');
  for (const [key,value] of requested) parameters.set(key,value);
  return parameters;
}

export function workspaceOption(parameters, name) {
  return parameters.has(name) && parameters.get(name) !== 'off';
}

export function workspaceViewport(parameters, view = globalThis) {
  if (parameters.has('responsive')) {
    const density = Number.isFinite(view.devicePixelRatio) && view.devicePixelRatio > 0 ? view.devicePixelRatio : 1;
    const uiScale = Math.max(.5, Math.min(4, density));
    return {
      width: Math.max(320, Math.min(7680, Math.round(view.innerWidth * uiScale))),
      height: Math.max(200, Math.min(4320, Math.round(view.innerHeight * uiScale))),
      uiScale,
    };
  }
  const width = Number(parameters.get('width') ?? 1280);
  const height = Number(parameters.get('height') ?? 720);
  const uiScale = Number(parameters.get('uiScale') ?? 1);
  if (!Number.isInteger(width) || !Number.isInteger(height) || width < 320 || height < 200 ||
      width > 7680 || height > 4320 || !Number.isFinite(uiScale) || uiScale < .5 || uiScale > 4)
    throw new Error('Invalid workspace viewport: width 320–7680, height 200–4320, uiScale 0.5–4');
  return {width, height, uiScale};
}
