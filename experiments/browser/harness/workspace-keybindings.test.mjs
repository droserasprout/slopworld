import {test} from 'node:test';
import assert from 'node:assert/strict';
import {installWorkspaceKeyBindings} from '../../../slopweb/runtime/wwwroot/workspace-keybindings.js';

function fixture(stored = null) {
  let current = stored;
  const view = {
    localStorage: {getItem: key => { assert.equal(key, 'slopworld.keybindings.v1'); return current; }, setItem: (key, value) => { assert.equal(key, 'slopworld.keybindings.v1'); current = value; }},
  };
  let loaded, persist;
  const probe = {
    WorkspaceKeyBindings: (json, callback) => { loaded = JSON.parse(json); persist = callback; },
  };
  return {view, probe, stored: () => current, loaded: () => loaded,
    save: value => persist(JSON.stringify(value))};
}

test('authored snapshot survives a reload and restores both slots', () => {
  const first = fixture();
  installWorkspaceKeyBindings(first.probe, first.view);
  assert.deepEqual(first.loaded(), {});
  first.save({SlopToggleOpenFiles: {a: 'G', b: 'None'}});
  const second = fixture(first.stored());
  installWorkspaceKeyBindings(second.probe, second.view);
  assert.deepEqual(second.loaded(), {SlopToggleOpenFiles: {a: 'G', b: 'None'}});
});

test('malformed, oversized and unknown version snapshots keep defaults', () => {
  for (const input of ['{', 'null', '[]', '{"version":2,"bindings":{"SlopTest":{"a":"G"}}}',
    '{"version":1,"bindings":[]}', 'x'.repeat(131073)]) {
    const f = fixture(input);
    installWorkspaceKeyBindings(f.probe, f.view);
    assert.deepEqual(f.loaded(), {});
  }
});

test('blocked reads and failed writes do not interrupt native controls', () => {
  for (const mode of ['read', 'write', 'access']) {
    const f = fixture();
    if (mode === 'access') Object.defineProperty(f.view, 'localStorage', {get() { throw Error('denied'); }});
    else f.view.localStorage[mode === 'read' ? 'getItem' : 'setItem'] = () => { throw Error('quota/disabled'); };
    assert.doesNotThrow(() => installWorkspaceKeyBindings(f.probe, f.view));
    assert.doesNotThrow(() => f.save({SlopToggleOpenFiles: {a: 'F9', b: 'F8'}}));
  }
});
