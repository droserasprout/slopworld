import {test} from 'node:test';
import assert from 'node:assert/strict';
import {attachWorkspaceCursor} from '../runtime/wwwroot/workspace-cursor.js';

test('cursor retains physical size and hotspot across density changes and viewport edges',()=>{
  const keys=['document','ImageData','devicePixelRatio','addEventListener'];
  const previous=new Map(keys.map(key=>[key,Object.getOwnPropertyDescriptor(globalThis,key)]));
  const overlay={style:{setProperty(key,value){this[key]=value;}},setAttribute(){},
    getContext(){return {putImageData(){}};},remove(){this.removed=true;}};
  const canvas=new EventTarget();canvas.style={cursor:'auto'};
  try {
    globalThis.document={createElement:()=>overlay,body:{append(){}}};
    globalThis.ImageData=class {};
    globalThis.devicePixelRatio=2;
    globalThis.addEventListener=()=>{};
    let update=JSON.stringify({id:1,width:48,height:48,x:4,y:6,pixels:''});
    const cursor=attachWorkspaceCursor(canvas,{WorkspaceCursorTake:()=>{const value=update;update='';return value;}});
    cursor.frame();
    const move=Object.assign(new Event('pointermove'),{clientX:999,clientY:600});
    canvas.dispatchEvent(move);
    assert.equal(canvas.style.cursor,'none');
    assert.equal(overlay.style.width,'24px');
    assert.equal(overlay.style.left,'997px');
    assert.equal(overlay.style.top,'597px');
    assert.equal(overlay.style.display,'block');
    globalThis.devicePixelRatio=1;cursor.frame();
    assert.equal(overlay.style.width,'48px');
    assert.equal(overlay.style.left,'995px');
    canvas.dispatchEvent(new Event('pointerleave'));
    assert.equal(overlay.style.display,'none');
    update='null';cursor.frame();assert.equal(canvas.style.cursor,'auto');
    cursor.dispose();assert.equal(overlay.removed,true);
  } finally {
    for(const [key,value] of previous) {if(value)Object.defineProperty(globalThis,key,value);else delete globalThis[key];}
  }
});
