import {test} from 'node:test';
import assert from 'node:assert/strict';
import {attachWorkspaceFullscreen} from '../../runtime/wwwroot/workspace-fullscreen.js';

function fixture() {
  const doc = new EventTarget();
  let request, enters=0, exits=0;
  const states=[], warnings=[];
  doc.documentElement = {requestFullscreen() { enters++; return Promise.resolve(); }};
  doc.fullscreenElement = null;
  doc.exitFullscreen = () => { exits++; return Promise.resolve(); };
  const managed = {WorkspaceWindow: callback => {request=callback;}, WorkspaceFullscreenState: value => states.push(value)};
  const controller = attachWorkspaceFullscreen(managed, {document:doc, console:{warn: (...args)=>warnings.push(args)}});
  return {doc, states, warnings, controller, request:value=>request(value), enters:()=>enters, exits:()=>exits,
    change(value) {doc.fullscreenElement=value?doc.documentElement:null;doc.dispatchEvent(new Event('fullscreenchange'));}};
}

test('native intent calls fullscreen synchronously and only confirmed DOM state checks the setting', async () => {
  const f=fixture();
  f.request(true);
  assert.equal(f.enters(),1);
  assert.deepEqual(f.states,[false]);
  await Promise.resolve();
  assert.equal(f.states.at(-1),false);
  f.change(true);
  assert.equal(f.states.at(-1),true);
  f.request(true);
  assert.equal(f.enters(),1);
  f.request(false);
  assert.equal(f.exits(),1);
  f.change(false);
  assert.equal(f.states.at(-1),false);
  f.controller.dispose();
});

test('Escape/browser exit updates native state and failed admission never leaves a checked checkbox', async () => {
  const f=fixture();
  f.change(true); f.change(false);
  assert.deepEqual(f.states,[false,true,false]);
  f.doc.documentElement.requestFullscreen=()=>Promise.reject(Error('activation denied'));
  f.request(true);
  await Promise.resolve();
  assert.equal(f.states.at(-1),false);
  assert.equal(f.warnings.length,1);
  f.doc.documentElement.requestFullscreen=()=>{throw Error('unsupported');};
  assert.doesNotThrow(()=>f.request(true));
  assert.equal(f.states.at(-1),false);
  f.controller.dispose();
});

test('teardown ignores delayed fullscreen completions and detaches native requests', async () => {
  const f=fixture();
  let complete;
  f.doc.documentElement.requestFullscreen=()=>new Promise(resolve=>{complete=resolve;});
  f.request(true); f.controller.dispose();
  f.change(true); complete(); await Promise.resolve(); f.request(true);
  assert.deepEqual(f.states,[false]);
});

test('delivered Escape exits only this workspace fullscreen and is consumed', () => {
  const f=fixture();
  const escape=()=>{const event=new Event('keydown',{cancelable:true});event.key='Escape';f.doc.dispatchEvent(event);return event;};
  assert.equal(escape().defaultPrevented,false);
  f.change(true);
  assert.equal(escape().defaultPrevented,true);
  assert.equal(f.exits(),1);
  f.controller.dispose();
  assert.equal(escape().defaultPrevented,false);
  assert.equal(f.exits(),1);
});
