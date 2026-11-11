import {test} from 'node:test';
import assert from 'node:assert/strict';
import {WorkspaceScheduler} from '../runtime/wwwroot/workspace-scheduler.js';

function fixture(draw=()=>true) {
  let time=0,id=0;
  const frames=new Map(),timers=new Map(),delays=new Map();
  const scheduler=new WorkspaceScheduler(draw,{
    raf:fn=>{frames.set(++id,fn);return id;},cancelRaf:id=>frames.delete(id),
    timer:(fn,delay)=>{timers.set(++id,fn);delays.set(id,delay);return id;},cancelTimer:id=>timers.delete(id),
    now:()=>time,
  });
  const run=map=>{assert.equal(map.size,1);const [key,fn]=map.entries().next().value;map.delete(key);fn();};
  return {scheduler,frames,timers,frame:()=>run(frames),timer:(delay=16)=>{
    assert.equal(timers.size,1);assert.equal(delays.get(timers.keys().next().value),delay);run(timers);
  },advance:ms=>{time+=ms;}};
}

test('browser scheduling functions are not invoked with the scheduler as their receiver',()=>{
  const previous=new Map(['requestAnimationFrame','cancelAnimationFrame'].map(key=>[key,Object.getOwnPropertyDescriptor(globalThis,key)]));
  try {
    globalThis.requestAnimationFrame=function(){assert.ok(this===undefined||this===globalThis);return 1;};
    globalThis.cancelAnimationFrame=function(){assert.ok(this===undefined||this===globalThis);};
    const scheduler=new WorkspaceScheduler(()=>false);
    scheduler.wake();scheduler.stop();
  } finally {
    for(const [key,descriptor] of previous) {
      if(descriptor)Object.defineProperty(globalThis,key,descriptor);
      else delete globalThis[key];
    }
  }
});

test('only repeated unchanged frames yield; changed frames and disabled mode stay active',()=>{
  let unchanged=true;
  const f=fixture(()=>unchanged);
  f.scheduler.setEnabled(true);
  f.frame();f.frame();
  assert.equal(f.timers.size,0);
  f.frame();assert.equal(f.frames.size,0);assert.equal(f.timers.size,1);
  f.timer();unchanged=false;f.frame();
  assert.equal(f.timers.size,0);assert.equal(f.frames.size,1);
  unchanged=true;f.scheduler.setEnabled(false);
  for(let i=0;i<6;i++)f.frame();
  assert.equal(f.timers.size,0);
  f.scheduler.stop();assert.equal(f.frames.size,0);
});

test('transport wakes cancel idle waits and coalesce; input holds continuous animation for 500 ms',()=>{
  const f=fixture();f.scheduler.setEnabled(true);
  f.frame();f.frame();f.frame();
  f.scheduler.wake();f.scheduler.wake();
  assert.equal(f.timers.size,0);assert.equal(f.frames.size,1);
  f.scheduler.wake(true);
  for(let i=0;i<5;i++)f.frame();
  assert.equal(f.timers.size,0);
  f.advance(500);f.frame();assert.equal(f.timers.size,1);
  f.scheduler.stop();f.scheduler.wake(true);
  assert.equal(f.timers.size,0);assert.equal(f.frames.size,0);
});

test('activity during native update never leaves both a timer and a frame scheduled',()=>{
  let wake=false;
  const f=fixture(()=>{if(wake)f.scheduler.wake();return true;});
  f.scheduler.setEnabled(true);f.frame();f.frame();
  wake=true;f.frame();
  assert.equal(f.frames.size,1);assert.equal(f.timers.size,0);
  f.scheduler.stop();
});

test('task yielding defaults off and inserts one task turn before each active frame',()=>{
  let draws=0;
  const f=fixture(()=>{draws++;return false;});
  assert.equal(f.scheduler.taskYield,false);
  f.scheduler.wake();f.frame();
  assert.equal(draws,1);assert.equal(f.frames.size,1);assert.equal(f.timers.size,0);
  assert.equal(f.scheduler.setTaskYield(true),false);
  for(let i=0;i<5;i++) {
    f.frame();
    assert.equal(draws,i+2);assert.equal(f.frames.size,0);assert.equal(f.timers.size,1);
    f.scheduler.request();assert.equal(f.frames.size,0);
    f.timer(0);
    assert.equal(draws,i+2);assert.equal(f.frames.size,1);assert.equal(f.timers.size,0);
  }
  f.scheduler.stop();
});

test('task yielding retains idle delay and interactive hold',()=>{
  const f=fixture();f.scheduler.setEnabled(true);f.scheduler.setTaskYield(true);
  f.frame();f.timer(0);f.frame();f.timer(0);f.frame();
  assert.equal(f.frames.size,0);f.timer(16);
  f.scheduler.wake(true);
  for(let i=0;i<5;i++){f.frame();f.timer(0);}
  f.advance(500);f.frame();f.timer(16);
  f.scheduler.stop();
});

test('activity cancels task waits and coalesces, including wakes during draw',()=>{
  let wake=false;
  const f=fixture(()=>{if(wake){f.scheduler.wake();f.scheduler.wake(true);}return true;});
  f.scheduler.setTaskYield(true);f.frame();
  f.scheduler.wake();f.scheduler.wake(true);
  assert.equal(f.timers.size,0);assert.equal(f.frames.size,1);
  wake=true;f.frame();
  assert.equal(f.timers.size,0);assert.equal(f.frames.size,1);
  wake=false;f.frame();f.timer(0);
  f.scheduler.stop();
});

test('disabling task yielding cancels its wait and returns the previous mode',()=>{
  const f=fixture(()=>false);
  assert.equal(f.scheduler.setTaskYield(true),false);f.frame();
  assert.equal(f.scheduler.setTaskYield(false),true);
  assert.equal(f.timers.size,0);assert.equal(f.frames.size,1);
  f.frame();assert.equal(f.timers.size,0);assert.equal(f.frames.size,1);
  assert.equal(f.scheduler.setTaskYield(false),false);
  f.scheduler.stop();
});

test('stop cancels task waits and pending frames without allowing restart',()=>{
  for(const waiting of [false,true]) {
    const f=fixture(()=>false);f.scheduler.setTaskYield(true);
    if(waiting)f.frame();
    f.scheduler.stop();f.scheduler.stop();f.scheduler.wake(true);f.scheduler.setTaskYield(false);
    assert.equal(f.frames.size,0);assert.equal(f.timers.size,0);
  }
});

test('stop during draw prevents another task wait even after activity requests a frame',()=>{
  const f=fixture(()=>{f.scheduler.wake(true);f.scheduler.stop();return true;});
  f.scheduler.setTaskYield(true);f.frame();
  assert.equal(f.frames.size,0);assert.equal(f.timers.size,0);
});
