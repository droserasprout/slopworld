import {test} from 'node:test';
import assert from 'node:assert/strict';
import {workspaceViewport,workspaceParameters,workspaceOption} from '../runtime/wwwroot/workspace-viewport.js';

test('root defaults are internal and explicit probe URLs remain explicit', () => {
  const root=Object.freeze({pathname:'/',search:''});
  const defaults=workspaceParameters(root);
  for(const key of ['entry','assets','eco','workspace','live','app','referenceAppearance','nativeFonts','responsive','idleYield','packetCache'])
    assert.equal(defaults.has(key),true,key);
  assert.equal(defaults.has('session'),false,'Default startup must work with any host catalog');
  assert.equal(root.search,'');
  const explicit=workspaceParameters({pathname:'/',search:'?session=fixture&width=1000&height=700&uiScale=1'});
  assert.equal(explicit.get('session'),'fixture');
  assert.equal(explicit.has('responsive'),false);
  assert.equal(workspaceParameters({pathname:'/index.html',search:'?gui'}).toString(),'gui=');
});

test('root performance defaults have explicit opt-outs while probes stay opt-in',()=>{
  for(const name of ['idleYield','packetCache']) {
    assert.equal(workspaceOption(workspaceParameters({pathname:'/',search:''}),name),true);
    assert.equal(workspaceOption(workspaceParameters({pathname:'/',search:'?'+name+'=off'}),name),false);
    assert.equal(workspaceOption(workspaceParameters({pathname:'/index.html',search:'?workspace'}),name),false);
  }
});

test('fixed geometry retains defaults and accepts the native reference scale', () => {
  assert.deepEqual(workspaceViewport(new URLSearchParams()), {width:1280,height:720,uiScale:1});
  assert.deepEqual(workspaceViewport(new URLSearchParams('width=2560&height=1440&uiScale=1.666666667')),
    {width:2560,height:1440,uiScale:1.666666667});
});
test('invalid geometry cannot allocate oversized surfaces or divide by an invalid scale', () => {
  for (const query of ['width=0','width=1.5','height=90000','uiScale=0','uiScale=NaN','width=Infinity'])
    assert.throws(() => workspaceViewport(new URLSearchParams(query)), /Invalid workspace viewport/);
});
test('responsive geometry follows browser size and density, with bounded allocation', () => {
  const parameters = new URLSearchParams('responsive');
  assert.deepEqual(workspaceViewport(parameters, {innerWidth:1536,innerHeight:864,devicePixelRatio:5/3}),
    {width:2560,height:1440,uiScale:5/3});
  assert.deepEqual(workspaceViewport(parameters, {innerWidth:800,innerHeight:600,devicePixelRatio:2}),
    {width:1600,height:1200,uiScale:2});
  assert.deepEqual(workspaceViewport(parameters, {innerWidth:10000,innerHeight:10000,devicePixelRatio:8}),
    {width:7680,height:4320,uiScale:4});
});
