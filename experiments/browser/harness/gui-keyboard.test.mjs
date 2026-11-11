import {test} from 'node:test';
import assert from 'node:assert/strict';
import {attachGuiInput} from '../runtime/wwwroot/gui-input.js';

function fixture() {
  const canvas=Object.assign(new EventTarget(),{width:100,height:100,getContext:()=>({clearRect(){},resetTransform(){}})});
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
