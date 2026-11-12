import {test} from 'node:test';
import assert from 'node:assert/strict';
import {loadBackground} from '../../runtime/wwwroot/loading-background.js';

test('workspace dimming shades the completed backdrop and leaves loading undimmed', async t => {
  const manifest = {
    cached: true, width: 100, height: 50, frames: ['frame'],
    animation: {onsetSeconds: 1, onset: 1, depths: 1, phases: 1, offsetX: 2, offsetY: 3},
  };
  let closed = 0;
  t.mock.method(globalThis, 'fetch', async url => String(url).endsWith('manifest.json')
    ? {ok: true, json: async () => manifest} : {ok: true, blob: async () => 'frame'});
  const location = Object.getOwnPropertyDescriptor(globalThis, 'location');
  const bitmap = Object.getOwnPropertyDescriptor(globalThis, 'createImageBitmap');
  Object.defineProperty(globalThis, 'location', {configurable: true, value: {href: 'http://localhost/'}});
  Object.defineProperty(globalThis, 'createImageBitmap', {
    configurable: true, value: async () => ({close: () => closed++}),
  });
  t.after(() => {
    for (const [key, descriptor] of [['location', location], ['createImageBitmap', bitmap]]) {
      if (descriptor) Object.defineProperty(globalThis, key, descriptor);
      else delete globalThis[key];
    }
  });
  const background = await loadBackground(new AbortController().signal);
  let calls = [], previous;
  const context = {
    globalAlpha: .7, fillStyle: '#abc',
    save() { previous = [this.globalAlpha, this.fillStyle]; },
    restore() { [this.globalAlpha, this.fillStyle] = previous; },
    drawImage() { calls.push(['frame', this.globalAlpha]); },
    fillRect(...rect) { calls.push(['shade', this.globalAlpha, this.fillStyle, ...rect]); },
  };
  background.draw(context, 200, 100);
  assert.deepEqual(calls, [['frame', 1], ['frame', .5]]);
  for (const dim of [0, .3, .65, .8]) {
    calls = [];
    background.draw(context, 200, 100, dim);
    assert.deepEqual(calls, [
      ['frame', 1], ['frame', .5],
      ...(dim ? [['shade', dim, '#000', 0, 0, 200, 100]] : []),
    ]);
    assert.equal(context.globalAlpha, .7);
    assert.equal(context.fillStyle, '#abc');
  }
  background.dispose();
  assert.equal(closed, 1);
});
