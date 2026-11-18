import {test} from 'node:test';
import assert from 'node:assert/strict';
import {attachWorkspaceFullscreen} from '../../runtime/wwwroot/workspace-fullscreen.js';
import {attachGuiInput} from '../../runtime/wwwroot/gui-input.js';
import {installInputDom,emit} from './input-dom-fixture.mjs';

function fixture(keyboard) {
  const doc = new EventTarget();
  let request, enters=0, exits=0, binding='F11';
  const states=[], warnings=[];
  doc.documentElement = {requestFullscreen() { enters++; return Promise.resolve(); }};
  doc.fullscreenElement = null;
  doc.exitFullscreen = () => { exits++; return Promise.resolve(); };
  const managed = {WorkspaceWindow: callback => {request=callback;}, WorkspaceFullscreenState: value => states.push(value),
    WorkspaceFullscreenKey:key=>key===binding};
  const controller = attachWorkspaceFullscreen(managed, {document:doc, navigator:{keyboard}, console:{warn: (...args)=>warnings.push(args)}});
  return {doc, states, warnings, controller, request:value=>request(value), enters:()=>enters, exits:()=>exits,
    bind(value){binding=value;},
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

test('Escape passes through while F11 exits workspace fullscreen', () => {
  const f=fixture();
  const escape=()=>{const event=new Event('keydown',{cancelable:true});event.key='Escape';f.doc.dispatchEvent(event);return event;};
  assert.equal(escape().defaultPrevented,false);
  f.change(true);
  assert.equal(escape().defaultPrevented,false);
  assert.equal(f.exits(),0);
  const f11=new Event('keydown',{cancelable:true});f11.key='F11';f.doc.dispatchEvent(f11);
  assert.equal(f11.defaultPrevented,true);
  assert.equal(f.exits(),1);
  f.controller.dispose();
  assert.equal(escape().defaultPrevented,false);
  assert.equal(f.exits(),1);
});

test('keyboard capture follows fullscreen lifetime and tolerates denial', async () => {
  const locks=[]; let unlocks=0;
  const f=fixture({lock: keys=>{locks.push(keys);return Promise.reject(Error('denied'));},unlock:()=>unlocks++});
  f.change(true);
  assert.deepEqual(locks,[['Escape','F11']]);
  f.change(true);
  assert.equal(locks.length,1);
  await Promise.resolve();
  f.change(false);
  assert.equal(unlocks,1);
  f.change(true);
  f.controller.dispose();
  assert.equal(unlocks,2);
});

test('document fallback honors rebinding and leaves a GUI-handled F11 alone',()=>{
  const f=fixture();
  try {
    for(const key of ['G',null]) {
      f.bind(key);
      const event=Object.assign(new Event('keydown',{cancelable:true}),{key:'F11'});
      f.doc.dispatchEvent(event);
      assert.equal(event.defaultPrevented,false);
    }
    f.bind('F11');
    const handled=Object.assign(new Event('keydown',{cancelable:true}),{key:'F11'});
    handled.preventDefault();f.doc.dispatchEvent(handled);
    assert.equal(f.enters(),0);
  } finally {f.controller.dispose();}
});

test('focused input delivers reassigned F11 and enters fullscreen once on its new binding',()=>{
  const canvas=installInputDom().canvas(), f=fixture(), keys=[];
  f.bind('G');
  const gui=attachGuiInput(canvas,{
    activationKey:key=>key==='G',
    GuiInputFrame:raw=>{
      for(const event of JSON.parse(raw)) {
        keys.push(event.key);
        if(event.key==='G')f.request(true);
      }
      return '{"commands":[]}';
    },
  });
  const press=(key,code)=>{
    const event=emit(gui.textInput,'keydown',{key,code});
    f.doc.dispatchEvent(event); // Bubble this event after the focused listener.
  };
  try {
    press('F11','F11');gui.frame();
    assert.deepEqual(keys,['F11']);
    assert.equal(f.enters(),0);
    press('g','KeyG');
    assert.deepEqual(keys,['F11','G']);
    assert.equal(f.enters(),1);
  } finally {gui.dispose();f.controller.dispose();}
});

test('late keyboard capture is released after disposal', async () => {
  let complete, unlocks=0;
  const f=fixture({lock:()=>new Promise(resolve=>{complete=resolve;}),unlock:()=>unlocks++});
  f.change(true); f.controller.dispose();
  complete(); await Promise.resolve();
  assert.equal(unlocks,2);
});
