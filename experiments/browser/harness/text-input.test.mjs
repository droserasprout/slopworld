import {test} from 'node:test';
import assert from 'node:assert/strict';
import {attachGuiInput} from '../runtime/wwwroot/gui-input.js';
import {attachInput} from '../runtime/wwwroot/input.js';
import {installInputDom,emit} from './input-dom-fixture.mjs';

function fixture() {
  const dom=installInputDom(),canvas=dom.canvas();
  const batches=[],devices=[];
  const device=attachInput(canvas,{InputFrame:value=>devices.push(JSON.parse(value))},true);
  const gui=attachGuiInput(canvas,{beforeFrame:()=>device.frame(),GuiInputFrame(value){batches.push(JSON.parse(value));return '{"commands":[]}';}});
  const target=gui.textInput;
  return {canvas,target,gui,device,batches,devices,dom,
    frame(){gui.frame();return batches.at(-1);},
    key(key,extra={}) {return emit(target,'keydown',{key,code:'KeyQ',isTrusted:true,shiftKey:false,ctrlKey:false,altKey:false,metaKey:false,...extra});},
    dispose(){device.dispose();gui.dispose();},
  };
}
const chars=batch=>String.fromCharCode(...batch.filter(e=>e.kind==='text').map(e=>e.character));

test('composition input before compositionend commits once without requiring trailing input',()=>{
  const f=fixture();
  try {
    f.canvas.focus();
    emit(f.target,'compositionstart');
    emit(f.target,'input',{data:'日本💡',inputType:'insertCompositionText',isComposing:true});
    assert.deepEqual(f.frame(),[]);
    emit(f.target,'compositionend',{data:'日本💡'});
    assert.equal(chars(f.frame()),'日本💡');
    assert.deepEqual(f.frame(),[]);
    f.key('q');
    emit(f.target,'input',{data:'q',inputType:'insertText'});
    assert.equal(chars(f.frame()),'q');
  } finally {f.dispose();}
});

test('both wheel adapters read deltaMode before deltas and preserve pixel, line and page units',()=>{
  const f=fixture();
  try {
    for(const [mode,guiScale,deviceScale] of [[0,.03,.01],[1,1,1/3],[2,100/16,1]]) {
      const event=new Event('wheel',{cancelable:true});
      let reads=0;
      Object.defineProperties(event,{
        deltaMode:{get(){reads++;return mode;}},
        deltaX:{get(){assert.ok(reads>0);return 6;}},
        deltaY:{get(){assert.ok(reads>0);return 12;}},
        clientX:{value:20},clientY:{value:30},
      });
      f.canvas.dispatchEvent(event);
      const batch=f.frame();
      assert.equal(event.defaultPrevented,true);
      assert.equal(batch[0].kind,'wheel');
      assert.equal(batch[0].dx,6*guiScale);
      assert.equal(batch[0].dy,12*guiScale);
      const device=f.devices.at(-1)[0];
      assert.equal(device.kind,'scroll');
      assert.equal(device.dx,-6*deviceScale);
      assert.equal(device.dy,-12*deviceScale);
    }
  } finally {f.dispose();}
});

test('canvas focus and pointer ownership use a shared editable proxy without a reset',()=>{
  const f=fixture();
  try {
    f.canvas.focus();
    assert.equal(document.activeElement,f.target);
    assert.equal(f.canvas.sibling,f.target);
    assert.deepEqual(f.frame(),[]);
    emit(f.canvas,'pointerdown',{clientX:20,clientY:30,button:0,buttons:1});
    assert.equal(document.activeElement,f.target);
    assert.equal(f.target.style.left,'20px');
    assert.equal(f.frame().some(e=>e.kind==='reset'),false);
    assert.equal(f.devices.at(-1).some(e=>e.kind==='reset'),false);
  }finally{f.dispose();}
});

test('preedit and Process/Enter key229 never reach native input; commit is ordered UTF-16 once',async()=>{
  const f=fixture();
  try {
    f.canvas.focus();
    emit(f.target,'compositionstart');
    for(const value of ['n','日本']) {
      emit(f.target,'compositionupdate',{data:value});
      emit(f.target,'input',{data:value,inputType:'insertCompositionText',isComposing:true});
      assert.equal(f.key('Process',{code:'Enter',keyCode:229,isComposing:true}).defaultPrevented,false);
      assert.equal(f.key('Enter',{code:'Enter',keyCode:229}).defaultPrevented,false);
      assert.deepEqual(f.frame(),[]);
      assert.deepEqual(f.devices.at(-1),value==='n'?[{kind:'reset'}]:[]);
    }
    const value='日本💡e\u0301';
    emit(f.target,'compositionend',{data:value});
    emit(f.target,'input',{data:value,inputType:'insertText'});
    f.key('Enter',{code:'Enter'});
    const batch=f.frame();
    assert.equal(chars(batch),value);
    assert.equal(batch.at(-1).key,'Return');
    assert.ok(batch.slice(0,-1).every(e=>e.kind==='text'&&!e.key&&!e.control&&!e.alt));
    assert.equal(f.target.value,'');
    await Promise.resolve();
    emit(f.target,'input',{data:value,inputType:'insertText'});
    assert.equal(chars(f.frame()),value);
  }finally{f.dispose();}
});

test('trusted typing uses DOM input once and keeps physical identity for native binding capture',()=>{
  const f=fixture();
  try {
    f.canvas.focus();
    assert.equal(f.key('q').defaultPrevented,false);
    assert.deepEqual(f.frame(),[]);
    emit(f.target,'input',{data:'q',inputType:'insertText'});
    assert.deepEqual(f.frame(),[{kind:'text',character:113,key:'Q',shift:false,control:false,alt:false,command:false}]);
    assert.equal(f.key('@',{ctrlKey:true,altKey:true,getModifierState:()=>true}).defaultPrevented,false);
    emit(f.target,'input',{data:'@',inputType:'insertText'});
    assert.deepEqual(f.frame(),[{kind:'text',character:64,shift:false,control:false,alt:false,command:false}]);
    f.key('q',{ctrlKey:true,altKey:true});
    assert.equal(f.frame()[0].key,'Q');
  }finally{f.dispose();}
});

test('empty composition cancels; blur cancels stale commits and cleanup is idempotent',()=>{
  const f=fixture();
  f.canvas.focus();
  emit(f.target,'compositionstart');
  emit(f.target,'compositionend',{data:''});
  assert.deepEqual(f.frame(),[]);
  emit(f.target,'compositionstart');
  emit(f.target,'blur',{relatedTarget:null});
  emit(f.target,'compositionend',{data:'stale'});
  emit(f.target,'input',{data:'stale',inputType:'insertText'});
  assert.deepEqual(f.frame(),[{kind:'reset'}]);
  f.canvas.focus();
  emit(f.target,'focus');
  emit(f.target,'input',{data:'fresh',inputType:'insertText'});
  assert.equal(chars(f.frame()),'fresh');
  f.device.dispose();
  assert.equal(f.target.removed,false);
  f.gui.dispose();f.gui.dispose();f.device.dispose();
  assert.equal(f.target.removed,true);
  const count=f.batches.length;
  emit(f.target,'compositionend',{data:'stale'});
  assert.equal(f.batches.length,count);
});

test('clipboard stays on the editable proxy in the initiating event',()=>{
  const dom=installInputDom(),canvas=dom.canvas();
  let batch,activation=false;
  const device=attachInput(canvas,{InputFrame(){}},true);
  const gui=attachGuiInput(canvas,{ClipboardTake:()=> '[]',GuiInputFrame(value){batch=JSON.parse(value);activation=true;return '{"commands":[]}';}});
  try {
    const event=emit(gui.textInput,'keydown',{key:'v',code:'KeyV',ctrlKey:true});
    assert.equal(event.defaultPrevented,false);
    emit(gui.textInput,'paste',{clipboardData:{getData:()=> '💡',items:[]}});
    assert.equal(activation,true);
    assert.deepEqual(batch,[{kind:'clipboard',text:'💡',image:false},{kind:'key',key:'V',control:true}]);
  }finally{gui.dispose();device.dispose();}
});

test('refocus does not revive a canceled composition or its trailing input',async()=>{
  const f=fixture();
  try {
    f.canvas.focus();
    emit(f.target,'compositionstart');
    emit(f.target,'blur',{relatedTarget:null});
    emit(f.target,'focus');
    emit(f.target,'compositionend',{data:'old pane'});
    emit(f.target,'input',{data:'old pane',inputType:'insertText'});
    assert.deepEqual(f.frame(),[{kind:'reset'}]);
    await Promise.resolve();
    emit(f.target,'input',{data:'fresh',inputType:'insertText'});
    assert.equal(chars(f.frame()),'fresh');
    emit(f.target,'compositionstart');
    emit(f.target,'compositionend',{data:'new composition'});
    assert.equal(chars(f.frame()),'new composition');
  } finally {f.dispose();}
});


test('window refocus resumes direct input and disposal prevents additional frames',()=>{
  const f=fixture();
  try {
    f.canvas.focus();
    emit(window,'blur');
    assert.deepEqual(f.frame(),[{kind:'reset'}]);
    emit(window,'focus');
    emit(f.target,'input',{data:'resumed',inputType:'insertText'});
    assert.equal(chars(f.frame()),'resumed');
    f.gui.dispose();
    const count=f.batches.length;
    assert.equal(f.gui.frame(),undefined);
    assert.equal(f.batches.length,count);
  }finally{f.dispose();}
});

test('window blur and hidden tabs release shared IME ownership before physical keys resume',()=>{
  for(const reason of ['window','hidden']) {
    const f=fixture();
    try {
      f.canvas.focus();
      emit(f.target,'compositionstart');
      f.frame();
      if(reason==='window')emit(window,'blur');
      else {document.hidden=true;emit(document,'visibilitychange');}
      assert.deepEqual(f.frame(),[{kind:'reset'}]);
      assert.deepEqual(f.devices.at(-1),[{kind:'reset'}]);
      document.hidden=false;
      emit(window,'focus');
      f.key('ArrowDown',{code:'ArrowDown'});
      assert.equal(f.frame()[0].key,'DownArrow');
      assert.deepEqual(f.devices.at(-1),[{kind:'key',key:'DownArrow',down:true}]);
    } finally {f.dispose();}
  }
});
