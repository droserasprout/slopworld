import {test} from 'node:test';
import assert from 'node:assert/strict';
import {attachWorkspaceCursor} from '../../runtime/wwwroot/workspace-cursor.js';

test('cursor retains one presentation at density, animation changes and edges',()=>{
  const keys=['document','ImageData','devicePixelRatio','addEventListener','CSS','innerWidth','innerHeight'];
  const previous=new Map(keys.map(key=>[key,Object.getOwnPropertyDescriptor(globalThis,key)]));
  const overlay={style:{setProperty(key,value){this[key]=value;}},setAttribute(){},
    getContext(){return {putImageData(){}};},toDataURL(){return 'data:image/png;base64,AA==';},remove(){this.removed=true;}};
  const canvas=new EventTarget();canvas.style={cursor:'auto'};
  try {
    globalThis.document={createElement:()=>overlay,body:{append(){}}};
    let decoded=0;
    globalThis.ImageData=class {constructor(){decoded++;}};
    globalThis.devicePixelRatio=2;
    globalThis.addEventListener=()=>{};
    globalThis.CSS={supports:()=>true};globalThis.innerWidth=1000;globalThis.innerHeight=700;
    let update=JSON.stringify({id:1,width:48,height:48,x:4,y:6,pixels:''});
    const cursor=attachWorkspaceCursor(canvas,{WorkspaceCursorTake:()=>{const value=update;update='';return value;}});
    cursor.frame();
    canvas.dispatchEvent(Object.assign(new Event('pointermove'),{clientX:500,clientY:300}));
    assert.equal(canvas.style.cursor,'none');assert.equal(overlay.style.display,'block');
    update=JSON.stringify({id:2,width:48,height:48,x:4,y:6,pixels:''});cursor.frame();
    assert.equal(canvas.style.cursor,'none');assert.equal(overlay.style.left,'498px');
    update=JSON.stringify({id:2,width:48,height:48,x:8,y:10,pixels:''});cursor.frame();
    assert.equal(decoded,2);assert.equal(overlay.style.left,'496px');assert.equal(overlay.style.top,'295px');
    update=JSON.stringify({id:2,width:48,height:48,x:4,y:6,pixels:''});cursor.frame();
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
