import test from 'node:test';
import assert from 'node:assert/strict';
import {yieldStartupPaint} from '../../runtime/wwwroot/startup-yield.js';

test('startup resumes in a task after animation callbacks, not in their microtasks',async()=>{
  const raf=globalThis.requestAnimationFrame,cancel=globalThis.cancelAnimationFrame;
  let callback,settled=false;
  globalThis.requestAnimationFrame=fn=>{callback=fn;return 1;};
  globalThis.cancelAnimationFrame=()=>{};
  try {
    const work=yieldStartupPaint().then(()=>{settled=true;});
    assert.equal(settled,false);
    callback();await Promise.resolve();
    assert.equal(settled,false);
    await work;assert.equal(settled,true);
  } finally {
    if(raf===undefined)delete globalThis.requestAnimationFrame;else globalThis.requestAnimationFrame=raf;
    if(cancel===undefined)delete globalThis.cancelAnimationFrame;else globalThis.cancelAnimationFrame=cancel;
  }
});

test('mandatory startup frames finish when animation callbacks are suspended',async()=>{
  const old=globalThis.requestAnimationFrame, cancel=globalThis.cancelAnimationFrame;
  let canceled;
  globalThis.requestAnimationFrame=()=>123;
  globalThis.cancelAnimationFrame=id=>{canceled=id;};
  try {
    await yieldStartupPaint(true);
    assert.equal(canceled,123);
  } finally {
    if(old===undefined)delete globalThis.requestAnimationFrame;else globalThis.requestAnimationFrame=old;
    if(cancel===undefined)delete globalThis.cancelAnimationFrame;else globalThis.cancelAnimationFrame=cancel;
  }
});
