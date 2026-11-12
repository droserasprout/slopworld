import test from 'node:test';
import assert from 'node:assert/strict';
import { Readable } from 'node:stream';
import { EventEmitter } from 'node:events';
import { daemonHttp } from '../../server/bridge.mjs';

function requestPair() {
  const req = Readable.from([]);
  Object.assign(req, { method: 'POST', url: '/host/api/workers', headers: {
    host: 'localhost:8787', origin: 'http://localhost:8787',
  } });
  const res = new EventEmitter();
  res.writeHead = status => { res.status = status; return res; };
  res.end = data => { res.data = data; return res; };
  return { req, res };
}

test('worker response after thirty seconds retains the managed deadline', async t => {
  t.mock.timers.enable({ apis: ['setTimeout'] });
  // AbortSignal.timeout uses internal timers; route it through the controlled clock.
  t.mock.method(AbortSignal, 'timeout', milliseconds => {
    const deadline = new AbortController();
    setTimeout(() => deadline.abort(), milliseconds);
    return deadline.signal;
  });
  let complete, signal, admitted;
  const ready = new Promise(resolve => { admitted = resolve; });
  t.mock.method(globalThis, 'fetch', async (_, options) => {
    signal = options.signal;
    admitted();
    return new Promise(resolve => { complete = resolve; });
  });
  const { req, res } = requestPair();
  const work = daemonHttp(req, res, 'http://daemon', 'host-token');
  await ready;
  t.mock.timers.tick(45000);
  assert.equal(signal.aborted, false);
  complete(new Response(Buffer.from([8, 1])));
  await work;
  assert.equal(res.status, 200);
  assert.deepEqual(res.data, Buffer.from([8, 1]));
  assert.equal(res.listenerCount('close'), 0);
});

test('browser cancellation aborts upstream without replaying the mutation', async t => {
  let admitted, calls = 0, signal;
  const ready = new Promise(resolve => { admitted = resolve; });
  t.mock.method(globalThis, 'fetch', async (_, options) => {
    calls++;
    signal = options.signal;
    admitted();
    return new Promise((_, reject) => signal.addEventListener('abort', () => reject(signal.reason)));
  });
  const { req, res } = requestPair();
  const work = daemonHttp(req, res, 'http://daemon', 'host-token');
  await ready;
  res.destroyed = true;
  res.emit('close');
  await work;
  assert.equal(signal.aborted, true);
  assert.equal(calls, 1);
  assert.equal(res.status, undefined);
  assert.equal(req.listenerCount('aborted'), 0);
});
