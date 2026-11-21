import {test} from 'node:test';
import assert from 'node:assert/strict';
import {attachGuiInput} from '../../runtime/wwwroot/gui-input.js';

import {installInputDom} from './input-dom-fixture.mjs';

function fixture() {
  const canvas=installInputDom().canvas();
  let batch;
  const input=attachGuiInput(canvas,{GuiInputFrame(value){batch=JSON.parse(value);return '{"commands":[]}';}});
  return {
    key(key,extra={}) {
      const event=Object.assign(new Event('keydown',{cancelable:true}),{key,code:'KeyQ',shiftKey:false,ctrlKey:false,altKey:false,metaKey:false,getModifierState:()=>false,...extra});
      canvas.dispatchEvent(event);input.frame();
      return {batch,prevented:event.defaultPrevented};
    },
    dispose:()=>input.dispose(),
  };
}

test('AltGr produces text once without leaking synthetic Ctrl+Alt or a physical binding',()=>{
  const f=fixture();
  try {
    for(const key of ['@','€','💡']) {
      const {batch,prevented}=f.key(key,{ctrlKey:true,altKey:true,getModifierState:name=>name==='AltGraph'});
      assert.equal(prevented,true);
      assert.equal(String.fromCharCode(...batch.map(e=>e.character)),key);
      assert.ok(batch.every(e=>e.kind==='text'&&!e.control&&!e.alt&&!e.command&&!e.key));
    }
  } finally {f.dispose();}
});

test('ordinary Ctrl+Alt shortcuts and composing keys retain their distinct paths',()=>{
  const f=fixture();
  try {
    assert.deepEqual(f.key('q',{ctrlKey:true,altKey:true}).batch,[{kind:'key',key:'Q',shift:false,control:true,alt:true,command:false}]);
    assert.deepEqual(f.key('@',{isComposing:true,getModifierState:()=>true}),{batch:[],prevented:false});
    assert.deepEqual(f.key('Dead',{getModifierState:()=>true}),{batch:[],prevented:false});
    assert.equal(f.key('q').batch[0].key,'Q');
  } finally {f.dispose();}
});

test('Alt plus a numpad digit keeps the Unity slot key with either Num Lock state',()=>{
  const f=fixture();
  try {
    const navigation=['Insert','End','ArrowDown','PageDown','ArrowLeft','Clear','ArrowRight','Home','ArrowUp','PageUp'];
    for(let digit=0;digit<=9;digit++)
      for(const key of [String(digit),navigation[digit]]) {
        const {batch,prevented}=f.key(key,{code:'Numpad'+digit,altKey:true});
        assert.equal(prevented,true);
        assert.deepEqual(batch,[{kind:'key',key:'Keypad'+digit,shift:false,control:false,alt:true,command:false}]);
      }
    assert.equal(f.key('End',{code:'Numpad1'}).batch[0].key,'End');
    assert.equal(f.key('1',{code:'Numpad1'}).batch[0].character,49);
  } finally {f.dispose();}
});

test('input profiling measures oldest queued event without changing the managed batch',()=>{
  const previousPerformance=globalThis.performance;
  let now=100;globalThis.performance={now:()=>now};
  const canvas=installInputDom().canvas(),samples=[];let batch;
  const input=attachGuiInput(canvas,{
    profileFrame:sample=>samples.push(sample),
    GuiInputFrame:value=>{batch=JSON.parse(value);return '{"commands":[]}';}
  });
  const press=()=>canvas.dispatchEvent(Object.assign(new Event('keydown',{cancelable:true}),{key:'q',code:'KeyQ'}));
  try {
    press();now=120;press();now=150;input.frame();
    assert.equal(samples.at(-1).inputQueueMs,50);
    assert.equal(batch.length,2);
    assert.ok(batch.every(event=>!('queuedAt' in event)));
    now=200;input.frame();assert.equal(samples.at(-1).inputQueueMs,0);
    press();now=300;window.dispatchEvent(new Event('blur'));
    now=310;input.frame();assert.equal(samples.at(-1).inputQueueMs,110);
    assert.equal(batch[0].character,113);
    assert.deepEqual(batch.slice(1),[{kind:'reset'}]);
  } finally {input.dispose();globalThis.performance=previousPerformance;}
});


test('workspace dispatches native fullscreen binding inside the initiating key event',()=>{
  const canvas=installInputDom().canvas();
  let dispatching=false, frames=0;
  const input=attachGuiInput(canvas,{
    activationKey:key=>key==='F11',
    GuiInputFrame(value) {
      const batch=JSON.parse(value);
      if(batch.some(event=>event.kind!=='reset')) {
        assert.equal(dispatching,true);
        assert.equal(batch[0].key,'F11');
        frames++;
      }
      return '{"commands":[]}';
    }
  });
  try {
    dispatching=true;
    const event=Object.assign(new Event('keydown',{cancelable:true}),{key:'F11',code:'F11'});
    canvas.dispatchEvent(event);
    dispatching=false;
    assert.equal(frames,1);
    assert.equal(event.defaultPrevented,true);
    input.frame();
    assert.equal(frames,1);
  } finally {input.dispose();}
});


test('ordinary synthetic and editable text stays batched; live printable fullscreen rebind dispatches eagerly',()=>{
  const canvas=installInputDom().canvas();
  let bound='F11',frames=[];
  const input=attachGuiInput(canvas,{
    activationKey:key=>key===bound,
    GuiInputFrame:value=>{frames.push(JSON.parse(value));return '{"commands":[]}';}
  });
  const key=(target,letter,trusted=false,shiftKey=false)=>{
    const event=new Event('keydown',{cancelable:true});
    for(const [name,value] of Object.entries({key:letter,code:'Key'+letter.toUpperCase(),isTrusted:trusted,shiftKey}))
      Object.defineProperty(event,name,{value});
    target.dispatchEvent(event);
    return event;
  };
  const edit=(letter)=>{
    const event=new Event('input');
    Object.defineProperty(event,'data',{value:letter});
    input.textInput.dispatchEvent(event);
  };
  try {
    for(const letter of 'abc')key(canvas,letter);
    for(const letter of 'def'){key(input.textInput,letter,true);edit(letter);}
    assert.equal(frames.length,0);
    input.frame();
    assert.equal(frames.length,1);
    assert.equal(String.fromCharCode(...frames[0].map(event=>event.character)),'abcdef');
    bound='G';
    const event=key(input.textInput,'g',true);
    assert.equal(frames.length,2);
    assert.deepEqual(frames[1].map(event=>({kind:event.kind,key:event.key})),[{kind:'key',key:'G'}]);
    assert.equal(event.defaultPrevented,true);
    bound='H';
    key(canvas,'g');
    assert.equal(frames.length,2);
    key(canvas,'h');
    assert.equal(frames.length,3);
    assert.equal(frames[2][0].character,'g'.charCodeAt(0));
    assert.equal(frames[2][1].key,'H');
    // Native shortcut handling bypasses Shift. The editable proxy still owns its
    // committed character, and synthetic input supplies that same character once.
    assert.equal(key(input.textInput,'H',true,true).defaultPrevented,false);
    edit('H');
    key(canvas,'H',false,true);
    assert.equal(frames.length,3);
    input.frame();
    assert.deepEqual(frames[3].map(e=>[e.kind,e.character,e.shift]),[['text',72,true],['text',72,true]]);
  } finally {input.dispose();}
});
