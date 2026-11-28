import test from 'node:test';
import assert from 'node:assert/strict';
import {attachWorkspaceAudio} from '../../runtime/wwwroot/workspace-audio.js';

class Decoder extends EventTarget {
  paused = true;
  src = '';
  released = 0;
  async play() { this.paused = false; }
  pause() { this.paused = true; }
  removeAttribute(name) { assert.equal(name, 'src'); this.src = ''; }
  load() { this.released++; }
  fail(code) { this.error = {code}; this.dispatchEvent(new Event('error')); }
}

const flush = () => new Promise(resolve => setImmediate(resolve));
function deferred() {
  let resolve, reject;
  const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
  return {promise, resolve, reject};
}
function fixture(t, options = {}) {
  const target = new EventTarget();
  target.navigator = {userAgent:'Mozilla/5.0 Firefox/132.0'};
  const state = {track:'pace-20261012', revision:1, play:true, volume:.4, station:'', streams:[]};
  const decoders = [], reports = [], titles = [];
  let name, submitStop, quits = 0;
  const beacons = [];
  target.navigator.sendBeacon = url => { beacons.push(url); return true; };
  const bridge = attachWorkspaceAudio({
    WorkspaceAudio:() => JSON.stringify(state),
    WorkspaceAudioInstall(value, submit) { name = value; submitStop = submit; },
    WorkspaceAudioReport(revision, playing, stream, title, error) {
      reports.push({revision, playing, stream, title, error});
    },
    WorkspaceAudioQuit() { quits++; if (options.exitUrl) assert.equal(submitStop(options.exitUrl), true); },
    WorkspaceAudioEnded(run) {
      assert.equal(run, state.revision);
      state.revision++;
      state.track = 'dive-20261012';
    },
  }, {
    target,
    createAudio() {
      const audio = new Decoder();
      options.prepare?.(audio, decoders.length);
      decoders.push(audio);
      return audio;
    },
    fetchTitle(url, {signal}) {
      const reply = deferred();
      titles.push({url, signal, reply});
      return reply.promise;
    },
  });
  t.after(() => bridge.dispose());
  function command(change) { Object.assign(state, change); state.revision++; bridge.frame(); }
  return {bridge, state, target, decoders, reports, titles, command, beacons,
    get name() { return name; }, get quits() { return quits; }};
}
const radio = {station:'station/a & b', streams:['first?x=1', 'alternative & 2']};
const relayUrl = audio => new URL(audio.src, 'https://workspace.example/colony/');

test('OST uses the hosted track, advances once and adjusts gain without replacing its decoder', async t => {
  const f = fixture(t);
  assert.equal(f.name, 'Firefox');
  f.bridge.frame();
  await flush();
  const [first] = f.decoders;
  assert.match(first.src, /assets\/ost\/pace-20261012\.ogg$/);
  f.state.volume = .13;
  f.bridge.frame();
  assert.equal(first.volume, .13);
  first.dispatchEvent(new Event('ended'));
  first.dispatchEvent(new Event('ended'));
  await flush();
  assert.equal(f.decoders.length, 2);
  assert.equal(first.released, 1);
  assert.match(f.decoders[1].src, /dive-20261012\.ogg$/);
  assert.equal(f.reports.at(-1).playing, true);
});

test('permission denial closes the relay and a gesture opens a new listener once', async t => {
  const f = fixture(t, {prepare(audio, index) {
    if (!index) audio.play = () => Promise.reject(new DOMException('Blocked', 'NotAllowedError'));
  }});
  f.command(radio);
  const first = relayUrl(f.decoders[0]);
  await flush();
  assert.match(f.reports.at(-1).error, /press a key/);
  assert.equal(f.decoders[0].src, '');
  for (let i = 0; i < 100; i++) f.bridge.frame();
  assert.equal(f.decoders.length, 1);
  f.target.dispatchEvent(new Event('keydown'));
  f.target.dispatchEvent(new Event('pointerdown'));
  await flush();
  assert.equal(f.decoders.length, 2);
  assert.notEqual(relayUrl(f.decoders[1]).searchParams.get('listener'), first.searchParams.get('listener'));
  assert.equal(f.reports.at(-1).playing, true);
  assert.equal(f.reports.at(-1).error, '');
});

test('an unsupported stream tries alternatives within the station and reports exhaustion', async t => {
  const f = fixture(t, {prepare(audio, index) {
    if (!index) audio.play = () => Promise.reject(new DOMException('Unsupported', 'NotSupportedError'));
  }});
  f.command(radio);
  const original = relayUrl(f.decoders[0]);
  assert.equal(original.origin, 'https://workspace.example');
  assert.equal(original.pathname, '/colony/host/media/radio');
  assert.equal(original.searchParams.get('station'), radio.station);
  assert.equal(original.searchParams.get('stream'), radio.streams[0]);
  await flush();
  const [first, second] = f.decoders;
  assert.equal(first.released, 1);
  const alternative = relayUrl(second);
  assert.equal(alternative.searchParams.get('station'), radio.station);
  assert.equal(alternative.searchParams.get('stream'), radio.streams[1]);
  assert.notEqual(alternative.searchParams.get('listener'), original.searchParams.get('listener'));
  assert.equal(f.reports.at(-1).stream, radio.streams[1]);
  second.fail(3);
  f.target.dispatchEvent(new Event('keydown'));
  f.bridge.frame();
  assert.equal(f.decoders.length, 2);
  assert.equal(second.released, 1);
  assert.match(f.reports.at(-1).error, /decode any stream/);
  assert.equal(f.quits, 0);
});

test('two local listeners have independent gain, titles and live connections through pause and resume', async t => {
  const a = fixture(t), b = fixture(t);
  a.command(radio);
  b.command({...radio, station:'second', volume:.21});
  await flush();
  const first = relayUrl(a.decoders[0]).searchParams.get('listener');
  const other = relayUrl(b.decoders[0]).searchParams.get('listener');
  assert.notEqual(first, other);
  const [oldTitle] = a.titles, [currentTitle] = b.titles;
  assert.equal(oldTitle.url, './host/media/radio/title?listener=' + first);
  b.state.volume = .54;
  b.bridge.frame();
  assert.equal(b.decoders[0].volume, .54);
  assert.equal(a.decoders[0].volume, .4);
  a.command({play:false});
  assert.equal(a.decoders[0].src, '');
  assert.equal(oldTitle.signal.aborted, true);
  assert.equal(currentTitle.signal.aborted, false);
  a.command({play:true});
  await flush();
  assert.notEqual(relayUrl(a.decoders[1]).searchParams.get('listener'), first);
  oldTitle.reply.resolve(Response.json({title:'Stale artist'}));
  currentTitle.reply.resolve(Response.json({title:'Current artist'}));
  await flush();
  assert.equal(a.reports.some(r => r.title === 'Stale artist'), false);
  assert.equal(b.reports.at(-1).title, 'Current artist');
  assert.equal(b.decoders[0].released, 0);
  a.bridge.dispose();
  assert.equal(a.titles[1].signal.aborted, true);
  assert.equal(b.decoders[0].paused, false);
});

test('late play completion, error and end cannot replace or pause the next source', async t => {
  const play = deferred();
  const f = fixture(t, {prepare(audio, index) {
    if (!index) audio.play = () => play.promise.then(() => { audio.paused = false; });
  }});
  f.command(radio);
  const [old] = f.decoders;
  f.command({station:'', streams:[]});
  old.fail(4);
  old.dispatchEvent(new Event('ended'));
  play.resolve();
  await flush();
  assert.equal(old.paused, true);
  assert.equal(f.decoders.length, 2);
  assert.equal(f.decoders[1].paused, false);
  assert.equal(f.reports.at(-1).playing, true);
  assert.equal(f.reports.at(-1).error, '');
});

test('network errors and live EOF release the stream and wait for explicit play', async t => {
  const f = fixture(t);
  f.command(radio);
  await flush();
  f.decoders[0].fail(2);
  assert.equal(f.decoders[0].src, '');
  assert.match(f.reports.at(-1).error, /connection/);
  f.target.dispatchEvent(new Event('pointerup'));
  assert.equal(f.decoders.length, 1);
  f.command({play:true});
  await flush();
  f.decoders[1].dispatchEvent(new Event('ended'));
  assert.match(f.reports.at(-1).error, /reconnect/);
  assert.equal(f.decoders[1].src, '');
});

test('system pause releases radio, and cached-page restoration preserves an explicit retry', async t => {
  const f = fixture(t);
  f.command(radio);
  await flush();
  const [audio] = f.decoders;
  audio.pause();
  audio.dispatchEvent(new Event('pause'));
  assert.equal(audio.src, '');
  assert.match(f.reports.at(-1).error, /paused/);
  f.target.dispatchEvent(Object.assign(new Event('pagehide'), {persisted:true}));
  f.target.dispatchEvent(new Event('pageshow'));
  f.bridge.frame();
  assert.equal(f.decoders.length, 1);
  assert.match(f.reports.at(-1).error, /paused/);
});

test('page cache suspension releases a listener and fatal cleanup skips shared shutdown policy', async t => {
  const f = fixture(t);
  f.command(radio);
  await flush();
  const id = relayUrl(f.decoders[0]).searchParams.get('listener');
  f.target.dispatchEvent(Object.assign(new Event('pagehide'), {persisted:true}));
  f.bridge.frame();
  assert.equal(f.decoders[0].src, '');
  assert.equal(f.titles[0].signal.aborted, true);
  assert.equal(f.quits, 0);
  f.target.dispatchEvent(new Event('pageshow'));
  await flush();
  assert.notEqual(relayUrl(f.decoders[1]).searchParams.get('listener'), id);
  f.bridge.dispose(false);
  assert.equal(f.decoders[1].src, '');
  f.bridge.dispose();
  assert.equal(f.quits, 0);
});

test('orderly exit queues the managed stop once across page cache restoration', t => {
  const exitUrl = 'https://workspace.example/colony/host/audio-stop?exit=current';
  const closing = fixture(t, {exitUrl});
  closing.target.dispatchEvent(Object.assign(new Event('pagehide'), {persisted:true}));
  assert.deepEqual(closing.beacons, []);
  closing.target.dispatchEvent(new Event('pageshow'));
  closing.bridge.dispose();
  closing.bridge.dispose();
  assert.equal(closing.quits, 1);
  assert.deepEqual(closing.beacons, [exitUrl]);
});
