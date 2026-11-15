import {test} from 'node:test';
import assert from 'node:assert/strict';
import {attachWorkspaceLinks} from '../../runtime/wwwroot/workspace-links.js';
import {attachGuiInput} from '../../runtime/wwwroot/gui-input.js';
import {installInputDom,emit} from './input-dom-fixture.mjs';

test('Ctrl-click dispatch opens the native URL before the pointer handler returns',()=>{
  const dom=installInputDom(),canvas=dom.canvas();
  const opened=[];
  let request;
  const links=attachWorkspaceLinks({WorkspaceOpenUrl: callback=>{request=callback;}},
    {open:(...args)=>opened.push(args)});
  const gui=attachGuiInput(canvas,{ClipboardTake:()=> '[]',GuiInputFrame(value){
    for(const event of JSON.parse(value))
      if(event.kind==='down' && event.control) request('https://example.com/path?q=1');
    return '{"commands":[]}';
  }});
  try {
    emit(canvas,'pointerdown',{clientX:20,clientY:30,button:0,buttons:1,ctrlKey:true});
    assert.deepEqual(opened,[['https://example.com/path?q=1','_blank','noopener,noreferrer']]);
    gui.frame();
    assert.equal(opened.length,1);
  } finally {gui.dispose();links.dispose();}
});

test('URL boundary rejects executable/local URLs and ignores requests after teardown',()=>{
  let request;
  const opened=[];
  const links=attachWorkspaceLinks({WorkspaceOpenUrl: callback=>{request=callback;}},
    {open:(...args)=>opened.push(args)});
  for(const url of ['javascript:alert(1)','data:text/html,hello','file:///etc/passwd','/relative','invalid']) request(url);
  assert.equal(opened.length,0);
  request('http://localhost:3000/');
  request('mailto:hello@example.com');
  assert.equal(opened.length,2);
  const stale=request;
  links.dispose();
  request('https://example.com');stale('https://example.com');
  assert.equal(opened.length,2);
});
