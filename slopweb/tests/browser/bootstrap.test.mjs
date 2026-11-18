import test from 'node:test';
import assert from 'node:assert/strict';

test('invalid viewport shows the error before the loading controller exists',async()=>{
  const classes=new Set(['workspace-app','loading']);
  const result={textContent:''};
  const view={};
  const previous=new Map();
  for(const [name,value] of Object.entries({
    window:view,location:{search:'?width=0'},
    document:{body:{classList:{remove:name=>classes.delete(name)}},getElementById:()=>result},
  })) {
    previous.set(name,Object.getOwnPropertyDescriptor(globalThis,name));
    Object.defineProperty(globalThis,name,{configurable:true,value});
  }
  try {
    await import('../../runtime/wwwroot/bootstrap.js');
    assert.equal(classes.has('loading'),false);
    assert.match(result.textContent,/Invalid workspace viewport/);
  } finally {
    for(const [name,descriptor] of previous) {
      if(descriptor)Object.defineProperty(globalThis,name,descriptor);
      else delete globalThis[name];
    }
  }
});
