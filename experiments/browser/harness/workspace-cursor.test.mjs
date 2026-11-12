import {test} from 'node:test';
import assert from 'node:assert/strict';
import {attachWorkspaceCursor} from '../../../slopweb/runtime/wwwroot/workspace-cursor.js';
import {cursorImage} from '../../../slopweb/runtime/wwwroot/cursor-image.js';

for(const supported of [true,false])test(`cursor retains size/hotspot at density and edges (native=${supported})`,()=>{
  const keys=['document','ImageData','devicePixelRatio','addEventListener','CSS','innerWidth','innerHeight'];
  const previous=new Map(keys.map(key=>[key,Object.getOwnPropertyDescriptor(globalThis,key)]));
  const overlay={style:{setProperty(key,value){this[key]=value;}},setAttribute(){},
    getContext(){return {putImageData(){}};},toDataURL(){return 'data:image/png;base64,AA==';},remove(){this.removed=true;}};
  const canvas=new EventTarget();canvas.style={cursor:'auto'};
  try {
    globalThis.document={createElement:()=>overlay,body:{append(){}}};
    globalThis.ImageData=class {};
    globalThis.devicePixelRatio=2;
    globalThis.addEventListener=()=>{};
    globalThis.CSS={supports:()=>supported};globalThis.innerWidth=1000;globalThis.innerHeight=700;
    let update=JSON.stringify({id:1,width:48,height:48,x:4,y:6,pixels:''});
    const cursor=attachWorkspaceCursor(canvas,{WorkspaceCursorTake:()=>{const value=update;update='';return value;}});
    cursor.frame();
    canvas.dispatchEvent(Object.assign(new Event('pointermove'),{clientX:500,clientY:300}));
    if(supported) {
      assert.match(canvas.style.cursor,/^image-set\(url\("data:image\/x-icon;base64,/);
      assert.match(canvas.style.cursor,/ 2x\), auto$/);
      assert.equal(overlay.style.display,'none');
    } else {
      assert.equal(canvas.style.cursor,'none');assert.equal(overlay.style.display,'block');
    }
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

test('CUR embeds the physical hotspot and unmodified PNG bytes',()=>{
  const png=Uint8Array.of(137,80,78,71,13,10,26,10);
  const encoded=cursorImage('data:image/png;base64,'+btoa(String.fromCharCode(...png)),48,48,4,6);
  const bytes=Uint8Array.from(atob(encoded.split(',')[1]),c=>c.charCodeAt(0));
  const header=new DataView(bytes.buffer);
  assert.equal(header.getUint16(2,true),2);assert.equal(header.getUint16(4,true),1);
  assert.equal(header.getUint16(10,true),4);assert.equal(header.getUint16(12,true),6);
  assert.equal(header.getUint32(18,true),22);assert.deepEqual(bytes.slice(22),png);
});
