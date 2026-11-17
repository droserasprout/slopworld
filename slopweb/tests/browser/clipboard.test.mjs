import test from 'node:test';
import {installInputDom,emit} from './input-dom-fixture.mjs';
import assert from 'node:assert/strict';
import {clipboardBridge} from '../../runtime/wwwroot/clipboard.js';
import {attachInput} from '../../runtime/wwwroot/input.js';
import {attachGuiInput} from '../../runtime/wwwroot/gui-input.js';

const tick = () => new Promise(resolve=>setImmediate(resolve));
const bytes = new Uint8Array([137,80,78,71,13,10,26,10]);
function fixture(requests) {
  const done=[], images=[];
  const managed={
    ClipboardTake:()=>JSON.stringify(requests.splice(0)),
    ClipboardComplete:(...args)=>done.push(args),
    ClipboardCompleteImage:(...args)=>images.push(args),
  };
  return {managed,done,images,bridge:clipboardBridge(managed)};
}
function navigatorWith(clipboard) {
  Object.defineProperty(globalThis,'navigator',{value:{clipboard},configurable:true});
}
installInputDom();
// Error banners should not hold the test runner open for eight seconds.
const originalTimeout=globalThis.setTimeout;
globalThis.setTimeout=(fn,delay)=>originalTimeout(fn,delay).unref();

test('DOM PNG is captured synchronously and delivered to its original request after async reading',async()=>{
  let resolve;
  let active=true;
  const f=fixture([{id:7,kind:'eventImage',images:true}]);
  f.bridge({clipboardData:{items:[{kind:'file',type:'image/png',getAsFile(){
    assert.ok(active);
    return {arrayBuffer:()=>new Promise(r=>resolve=r)};
  }}]}});
  active=false;
  f.managed.ClipboardTake=()=> '[]'; // Session changes never create a second V.
  resolve(bytes.buffer);
  await tick();
  assert.deepEqual(f.images,[[7,Buffer.from(bytes).toString('base64'),'']]);
  assert.deepEqual(f.done,[]);
});

test('context read prefers text across all offered items before image',async()=>{
  navigatorWith({read:async()=>[
    {types:['image/png'],getType:()=>{throw Error('text should win')}},
    {types:['text/plain'],getType:async()=>new Blob(['hello'])},
  ]});
  const f=fixture([{id:8,kind:'read',images:true}]); f.bridge(); await tick();
  assert.deepEqual(f.done,[[8,'hello','']]); assert.deepEqual(f.images,[]);
});

test('empty text falls through to PNG while text-only request never reads images',async()=>{
  navigatorWith({read:async()=>[{types:['text/plain','image/png'],getType:async type=>
    type === 'text/plain' ? new Blob(['']) : new Blob([bytes])}],readText:async()=> 'host text'});
  const f=fixture([{id:9,kind:'read',images:true},{id:10,kind:'read',images:false}]);
  f.bridge(); await tick();
  assert.deepEqual(f.images,[[9,Buffer.from(bytes).toString('base64'),'']]);
  assert.deepEqual(f.done,[[10,'host text','']]);
});

test('denied permission and missing event PNG settle the original requests as errors',async()=>{
  navigatorWith({read:async()=>{throw Error('permission denied')}});
  const f=fixture([{id:11,kind:'read',images:true},{id:12,kind:'eventImage',images:true}]);
  f.bridge(); await tick();
  assert.equal(f.done.length,2);
  assert.deepEqual(f.done.map(x=>x[0]).sort(),[11,12]);
  assert.ok(f.done.every(x=>x[1] === '' && x[2])); assert.deepEqual(f.images,[]);
});

test('copy still writes text and completes once',async()=>{
  let copied;
  navigatorWith({writeText:async text=>{copied=text}});
  const f=fixture([{id:13,kind:'write',text:'copy'}]); f.bridge(); await tick();
  assert.equal(copied,'copy'); assert.deepEqual(f.done,[[13,'','']]);
});

test('PRIMARY copy selects proxy text synchronously without writing CLIPBOARD or sending text input',()=>{
  const canvas=installInputDom().canvas(), batches=[], done=[];
  const requests=[{id:14,kind:'primary',text:'selected terminal text'}];
  navigatorWith({writeText:()=>{assert.fail('PRIMARY must not overwrite CLIPBOARD');}});
  const gui=attachGuiInput(canvas,{
    GuiInputFrame:raw=>{batches.push(JSON.parse(raw));return '{"commands":[]}';},
    ClipboardTake:()=>JSON.stringify(requests.splice(0)),
    ClipboardComplete:(...args)=>done.push(args),
  });
  let selected;
  gui.textInput.select=()=>{selected=gui.textInput.value;};
  emit(canvas,'pointerup',{button:0,clientX:10,clientY:20});
  assert.equal(selected,'selected terminal text');
  assert.deepEqual(done,[[14,'','']]);
  gui.frame();
  assert.deepEqual(batches[1],[]);
  emit(gui.textInput,'input',{data:'x',inputType:'insertText'});
  gui.frame();
  assert.deepEqual(batches[2].map(event=>event.character),[120]);
  assert.equal(gui.textInput.value,'');
  gui.dispose();
});

test('DOM paste dispatches native V before asynchronous image conversion',async()=>{
  const f=fixture([]), batches=[];
  const canvas=installInputDom().canvas();
  let requests=[];
  f.managed.GuiInputFrame=raw=>{
    batches.push(JSON.parse(raw));
    if (batches.at(-1).some(x=>x.kind === 'clipboard')) requests.push({id:42,kind:'eventImage',images:true});
    return '{"commands":[]}';
  };
  f.managed.ClipboardTake=()=>JSON.stringify(requests.splice(0));
  const input=attachGuiInput(canvas,f.managed);
  const paste=emit(input.textInput,'paste',{clipboardData:{getData:()=>'',items:[
    {kind:'file',type:'image/png',getAsFile:()=>new Blob([bytes])},
  ]}});
  assert.ok(paste.defaultPrevented);
  assert.deepEqual(batches[0],[{kind:'clipboard',text:'',image:true},{kind:'key',key:'V',control:true}]);
  assert.deepEqual(f.images,[]);
  await tick();
  assert.deepEqual(f.images,[[42,Buffer.from(bytes).toString('base64'),'']]);
  assert.equal(batches.length,1);
  input.dispose();
});


test('synchronous pointer frames pump physical input before native selection dispatch',()=>{
  const order=[];
  const canvas=installInputDom().canvas();
  const input=attachGuiInput(canvas,{
    beforeFrame:()=>order.push('physical'),
    GuiInputFrame:()=>{order.push('native');return '{"commands":[]}';},
    ClipboardTake:()=> '[]',
  });
  emit(canvas,'pointerdown',{clientX:0,clientY:0,button:0});
  assert.deepEqual(order,['physical','native']);
  input.dispose();
});

test('middle click reaches native tab hit testing and cancels browser default when no PRIMARY is requested',()=>{
  const canvas=installInputDom().canvas(), batches=[], physicalBatches=[];
  const physical=attachInput(canvas,{InputFrame:raw=>physicalBatches.push(JSON.parse(raw))},true);
  const gui=attachGuiInput(canvas,{
    beforeFrame:()=>physical.frame(),
    GuiInputFrame:raw=>{batches.push(JSON.parse(raw));return '{"commands":[]}';},
    ClipboardTake:()=> '[]',
  });
  for(const type of ['pointerdown','pointerup'])
    assert.ok(emit(canvas,type,{button:1,clientX:10,clientY:20}).defaultPrevented);
  assert.deepEqual(batches.map(batch=>[batch[0].kind,batch[0].button]),[['down',2],['up',2]]);
  assert.deepEqual(physicalBatches.map(batch=>[batch[0].button,batch[0].down]),[[2,true],[2,false]]);
  gui.dispose();physical.dispose();
});

test('native PRIMARY request receives DOM paste once without a duplicate V or stale local paste',()=>{
  const canvas=installInputDom().canvas(), batches=[], done=[];
  let requests=[];
  const gui=attachGuiInput(canvas,{
    GuiInputFrame:raw=>{
      const batch=JSON.parse(raw);batches.push(batch);
      if(batch.some(event=>event.kind==='down' && event.button===2))requests.push({id:43,kind:'readPrimary'});
      return '{"commands":[]}';
    },
    ClipboardTake:()=>JSON.stringify(requests.splice(0)),
    ClipboardComplete:(...args)=>done.push(args),
  });
  for(const type of ['pointerdown','pointerup'])
    assert.equal(emit(canvas,type,{button:1,clientX:10,clientY:20}).defaultPrevented,false);
  emit(gui.textInput,'paste',{clipboardData:{getData:()=> 'desktop selection',items:[]}});
  assert.deepEqual(done,[[43,'desktop selection','']]);
  assert.equal(batches.flat().some(event=>event.key==='V'),false);
  gui.dispose();
});

test('PRIMARY selection uses the editor command and survives window blur',()=>{
  const {canvas:makeCanvas,document}=installInputDom(), done=[];
  let requests=[{id:44,kind:'primary',text:'keep on blur'}], selected;
  document.execCommand=command=>{assert.equal(command,'selectAll');selected=document.activeElement.value;return true;};
  const gui=attachGuiInput(makeCanvas(),{
    GuiInputFrame:()=>'{"commands":[]}',
    ClipboardTake:()=>JSON.stringify(requests.splice(0)),
    ClipboardComplete:(...args)=>done.push(args),
  });
  gui.frame();emit(window,'blur');
  assert.equal(selected,'keep on blur');assert.equal(gui.textInput.value,'keep on blur');
  assert.deepEqual(done,[[44,'','']]);gui.dispose();
});

test('abandoned PRIMARY reads settle once and cannot capture a later normal paste',()=>{
  const f=fixture([{id:45,kind:'readPrimary'}]);f.bridge();
  assert.equal(f.bridge.primaryPending,true);f.bridge.cancelPrimary();f.bridge.cancelPrimary();
  assert.equal(f.bridge.pastePrimary({clipboardData:{getData:()=> 'later'}}),false);
  assert.deepEqual(f.done,[[45,'','PRIMARY paste canceled']]);
});

test('keyboard paste after a missed middle-click paste uses ordinary routing',()=>{
  const canvas=installInputDom().canvas(), batches=[], done=[], requests=[];
  const gui=attachGuiInput(canvas,{
    GuiInputFrame:raw=>{
      const batch=JSON.parse(raw);batches.push(batch);
      if(batch.some(event=>event.kind==='down')) requests.push({id:46,kind:'readPrimary'});
      return '{"commands":[]}';
    },
    ClipboardTake:()=>JSON.stringify(requests.splice(0)),
    ClipboardComplete:(...args)=>done.push(args),
  });
  for(const type of ['pointerdown','pointerup','auxclick'])
    emit(canvas,type,{button:1,clientX:0,clientY:0});
  emit(gui.textInput,'keydown',{code:'KeyV',key:'v',ctrlKey:true});
  emit(gui.textInput,'paste',{clipboardData:{getData:()=> 'ordinary',items:[]}});
  assert.deepEqual(done,[[46,'','PRIMARY paste canceled']]);
  assert.deepEqual(batches.at(-1),[{kind:'clipboard',text:'ordinary',image:false},{kind:'key',key:'V',control:true}]);
  gui.dispose();
});

test('an unanswered auxiliary click expires only its own PRIMARY request',async()=>{
  const requests=[{id:47,kind:'readPrimary'}], f=fixture(requests);
  f.bridge();f.bridge.expirePrimary();
  requests.push({id:48,kind:'readPrimary'});f.bridge();
  await new Promise(resolve=>originalTimeout(resolve,0));
  assert.equal(f.bridge.primaryPending,true);
  f.bridge.expirePrimary();
  await new Promise(resolve=>originalTimeout(resolve,0));
  assert.equal(f.bridge.primaryPending,false);
  assert.deepEqual(f.done,[[47,'','PRIMARY paste canceled'],[48,'','PRIMARY paste canceled']]);
});
