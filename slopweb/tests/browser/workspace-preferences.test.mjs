import {test} from 'node:test';
import assert from 'node:assert/strict';
import {installWorkspacePreferences} from '../../runtime/wwwroot/workspace-preferences.js';

const key = 'slopworld.presentation.v1';

function storage() {
  const entries = new Map();
  return {getItem: key => entries.get(key) ?? null, setItem: (key, value) => entries.set(key, value)};
}

function tab(localStorage, save = () => {}) {
  const events = new Map(), documentEvents = new Map();
  const view = {
    get localStorage() {
      if (!localStorage) throw Error('denied');
      return localStorage;
    },
    addEventListener: (name, handler) => events.set(name, handler),
    document: {visibilityState: 'visible', addEventListener: (name, handler) => documentEvents.set(name, handler)},
  };
  let read, persist;
  const probe = {
    WorkspacePreferences: (load, write) => { read = load; persist = write; },
    WorkspaceSavePreferences: () => save(persist),
  };
  installWorkspacePreferences(probe, view);
  return {read, persist, view, pagehide: events.get('pagehide'), visibility: documentEvents.get('visibilitychange')};
}

test('overlapping tabs flush independent fields and reload saved values', () => {
  const origin = storage();
  origin.setItem(key + '.uiScheme', JSON.stringify('custom'));
  origin.setItem(key + '.fontSize', '19');
  let flushes = 0;
  const first = tab(origin);
  const second = tab(origin, persist => {
    flushes++;
    persist('fontSize', '22');
  });
  assert.equal(second.read('fontSize'), '19');
  second.visibility();
  assert.equal(flushes, 0);
  assert.equal(first.persist('radioHiddenSources', JSON.stringify('ost\nspotify')), true);
  second.view.document.visibilityState = 'hidden';
  second.visibility();
  assert.equal(flushes, 1);
  second.pagehide();
  assert.equal(flushes, 2);

  const reloaded = tab(origin);
  assert.equal(JSON.parse(reloaded.read('radioHiddenSources')), 'ost\nspotify');
  assert.equal(reloaded.read('fontSize'), '22');
  assert.equal(JSON.parse(reloaded.read('uiScheme')), 'custom');
});

test('missing and oversized fields keep defaults', () => {
  const origin = storage();
  origin.setItem(key, JSON.stringify({version: 1, preferences: {theme: 'custom'}}));
  const context = tab(origin);
  assert.equal(context.read('theme'), '');
  origin.setItem(key + '.theme', 'x'.repeat(131073));
  assert.equal(context.read('theme'), '');
});

test('blocked reads and failed writes do not interrupt native controls', () => {
  for (const mode of ['read', 'write', 'access']) {
    const origin = storage();
    if (mode === 'read') origin.getItem = () => { throw Error('disabled'); };
    else origin.setItem = () => { throw Error('quota'); };
    const context = tab(mode === 'access' ? null : origin);
    assert.equal(context.read('theme'), '');
    assert.equal(context.persist('theme', '"custom"'), mode === 'read');
  }
});
