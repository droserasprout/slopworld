import test from 'node:test';
import assert from 'node:assert/strict';
import {jukeboxLikes, watchJukeboxLikes} from '../../runtime/wwwroot/jukebox-likes.js';

const key = 'slopworld.jukebox.likes.v1';
const first = '[[like]]\ntitle = "first"\n\n';
const second = '[[like]]\ntitle = "second"\n\n';

function origin() {
  const values = new Map();
  const localStorage = {
    getItem: name => values.get(name) ?? null,
    setItem: (name, text) => values.set(name, text),
  };
  let pending = Promise.resolve();
  const locks = {request(name, callback) {
    assert.equal(name, key);
    const result = pending.then(callback);
    pending = result.catch(() => {});
    return result;
  }};
  return () => Object.assign(new EventTarget(), {localStorage, navigator: {locks}});
}

test('two pending Likes survive both tab reloads, in commit order', async () => {
  const tab = origin();
  const a = jukeboxLikes(tab()), b = jukeboxLikes(tab());
  assert.equal(a.read(), '');
  await Promise.all([a.write(null, first), b.write(null, second)]);
  assert.equal(jukeboxLikes(tab()).read(), first + second);
  assert.equal(a.read(), b.read());
});

test('raw history edits and deletion persist, and a stale editor preserves other tabs’ writes', async () => {
  const tab = origin();
  const a = jukeboxLikes(tab()), b = jukeboxLikes(tab());
  await a.write(null, first);
  const original = b.read();
  await a.write(null, second);
  await assert.rejects(b.write(original, ''), /History changed/);
  assert.equal(b.read(), first + second);

  const edited = '# edited file with no final newline';
  await b.write(b.read(), edited);
  await a.write(null, first);
  assert.equal(jukeboxLikes(tab()).read(), edited + '\n' + first);
  await b.write(b.read(), '');
  assert.equal(jukeboxLikes(tab()).read(), '');
});

test('write denial is reported and leaves the previous history available for retry', async () => {
  const view = origin()(), store = jukeboxLikes(view);
  await store.write(null, first);
  const save = view.localStorage.setItem;
  view.localStorage.setItem = () => { throw Error('quota exceeded'); };
  await assert.rejects(store.write(null, second), /quota exceeded/);
  assert.equal(store.read(), first);
  view.localStorage.setItem = save;
  await store.write(null, second);
  assert.equal(store.read(), first + second);
  Object.defineProperty(view, 'localStorage', {get() { throw Error('access denied'); }});
  assert.throws(store.read, /access denied/);
  await assert.rejects(store.write(null, first), /access denied/);
});

test('open history refreshes on peer writes, storage clear and cached page restoration', () => {
  const view = origin()(), lifetime = new AbortController();
  let reloads = 0;
  watchJukeboxLikes(view, () => reloads++, lifetime.signal);
  const storage = (name, area = view.localStorage) =>
    view.dispatchEvent(Object.assign(new Event('storage'), {key: name, storageArea: area}));
  storage('other');
  storage(key, {});
  storage(key);
  storage(null);
  view.dispatchEvent(Object.assign(new Event('pageshow'), {persisted:true}));
  assert.equal(reloads, 3);
  lifetime.abort();
  storage(key);
  assert.equal(reloads, 3);
});
