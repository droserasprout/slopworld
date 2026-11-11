import {test} from 'node:test';
import assert from 'node:assert/strict';
import {installWorkspacePreferences} from '../runtime/wwwroot/workspace-preferences.js';

function fixture(stored = null) {
  const events = new Map(), documentEvents = new Map();
  let current = stored;
  const view = {
    localStorage: {getItem: () => current, setItem: (_key, value) => { current = value; }},
    addEventListener: (name, handler) => events.set(name, handler),
    document: {visibilityState: 'visible', addEventListener: (name, handler) => documentEvents.set(name, handler)},
  };
  let loaded, persist, saves = 0;
  const probe = {
    WorkspacePreferences: (json, callback) => { loaded = JSON.parse(json); persist = callback; },
    WorkspaceSavePreferences: () => { saves++; },
  };
  return {view, probe, events, documentEvents, stored: () => current, loaded: () => loaded,
    save: value => persist(JSON.stringify(value)), saves: () => saves};
}

test('native snapshot survives a reload and overlays initial host defaults', () => {
  const first = fixture();
  installWorkspacePreferences(first.probe, first.view);
  assert.deepEqual(first.loaded(), {});
  first.save({uiScheme: 'custom', sidebarHidden: true, fontSize: 19});
  const second = fixture(first.stored());
  installWorkspacePreferences(second.probe, second.view);
  assert.deepEqual(second.loaded(), {uiScheme: 'custom', sidebarHidden: true, fontSize: 19});
});

test('malformed, oversized and unknown version snapshots keep defaults', () => {
  for (const input of ['{', 'null', '[]', '{"version":2,"preferences":{"theme":"bad"}}',
    '{"version":1,"preferences":[]}', 'x'.repeat(131073)]) {
    const f = fixture(input);
    installWorkspacePreferences(f.probe, f.view);
    assert.deepEqual(f.loaded(), {});
  }
});

test('blocked reads and failed writes do not interrupt native controls', () => {
  for (const mode of ['read', 'write', 'access']) {
    const f = fixture();
    if (mode === 'access') Object.defineProperty(f.view, 'localStorage', {get() { throw Error('denied'); }});
    else f.view.localStorage[mode === 'read' ? 'getItem' : 'setItem'] = () => { throw Error('quota/disabled'); };
    assert.doesNotThrow(() => installWorkspacePreferences(f.probe, f.view));
    assert.doesNotThrow(() => f.save({theme: 'custom'}));
  }
});

test('pagehide and backgrounding flush changes before native debounce', () => {
  const f = fixture();
  installWorkspacePreferences(f.probe, f.view);
  f.documentEvents.get('visibilitychange')();
  assert.equal(f.saves(), 0);
  f.view.document.visibilityState = 'hidden';
  f.documentEvents.get('visibilitychange')();
  f.events.get('pagehide')();
  assert.equal(f.saves(), 2);
});
