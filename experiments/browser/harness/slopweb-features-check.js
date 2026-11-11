// Run only in an invocation-owned browser. Restore presentation settings;
// do not send terminal input or alter host audio source selection.
(async () => {
  const probe = window.__managedProbe, canvas = document.querySelector('canvas');
  if (window.__workspace?.status !== 'running') throw Error('Workspace is not running');
  const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
  const until = async condition => {
    const deadline = Date.now() + 180000;
    while (!condition()) {
      if (Date.now() > deadline) throw Error('Feature check timed out');
      await wait(250);
    }
  };
  const state = () => JSON.parse(probe.WorkspaceState());
  const fonts = () => JSON.parse(probe.WorkspaceFontSnapshot());
  probe.WorkspaceSavePreferences();
  const key = 'slopworld.presentation.v1', stored = localStorage.getItem(key);
  const original = JSON.parse(stored).preferences;
  const requests = () => performance.getEntriesByType('resource').filter(entry => entry.name.endsWith('/host/fonts')).length;
  const initialRequests = requests();
  if (initialRequests !== 0) throw Error('Unchanged startup unnecessarily prepared fonts');
  const observing = probe.WorkspaceObserveLabels(true);
  const decoder = new Audio();
  try {
    const initial = state(), box = canvas.getBoundingClientRect();
    const [x,y,w,h] = initial.doors.config.rect;
    canvas.dispatchEvent(new PointerEvent('pointermove', {
      clientX:box.left+(x+w/2)*initial.uiScale*box.width/canvas.width,
      clientY:box.top+(y+h/2)*initial.uiScale*box.height/canvas.height,
    }));
    await until(() => JSON.parse(probe.WorkspaceLabels()).some(label => label.text === 'Settings'));
    const hover = true;
    canvas.dispatchEvent(new PointerEvent('pointermove', {clientX:box.right-5,clientY:box.bottom-5}));
    const before = fonts(), frame = state().frame;
    const size = [19,21,23,25,27].find(value => !before.faces.some(face => face.size === value));
    if (!size) throw Error('No unused test size');
    probe.WorkspaceAppearance(JSON.stringify({fontSize:size}));
    probe.WorkspaceSavePreferences();
    await until(() => fonts().faces.some(face => face.size === size));
    if (state().frame <= frame + 2) throw Error('UI frames stopped while fonts loaded');
    if (document.getElementById('font-status')) throw Error('Font loader reported failure');
    const loaded = fonts();
    const loadingFrames = state().frame-frame;
    probe.WorkspaceAppearance(JSON.stringify({fontSize:original.fontSize}));
    probe.WorkspaceSavePreferences();
    await wait(1000);
    const revision = fonts().atlasVersion;
    const requestCount = requests();
    for (let i=0;i<5;i++) probe.WorkspaceSavePreferences();
    await wait(1000);
    if (fonts().atlasVersion !== revision) throw Error('Unchanged settings reloaded fonts');
    if (requests() !== requestCount) throw Error('Unchanged settings requested fonts');
    decoder.preload = 'auto';
    const ready = new Promise((resolve,reject) => {
      const timer = setTimeout(() => reject(Error('OST decode timed out')),30000);
      decoder.addEventListener('loadeddata', () => {clearTimeout(timer);resolve();},{once:true});
      decoder.addEventListener('error', () => {clearTimeout(timer);reject(Error('OST decode failed'));},{once:true});
    });
    decoder.src = './assets/ost/pace-20261012.ogg';
    decoder.load(); await ready;
    if (!(decoder.duration > 0)) throw Error('OST has no duration');
    return {hover, dynamicFonts:true, oldFaces:before.faces.length, newFaces:loaded.faces.length,
      uiFramesDuringLoad:loadingFrames, startupFontRequests:initialRequests, unchangedSettingsCached:true,
      ostDecoded:true, ostDuration:decoder.duration, audio:JSON.parse(probe.WorkspaceAudio())};
  } finally {
    decoder.pause();decoder.removeAttribute('src');decoder.load();
    probe.WorkspaceAppearance(JSON.stringify({fontSize:original.fontSize}));
    probe.WorkspaceSavePreferences();localStorage.setItem(key,stored);
    probe.WorkspaceObserveLabels(observing);
  }
})()
