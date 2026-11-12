import test from 'node:test';
import assert from 'node:assert/strict';
import {GuiTargetDamagePlanner, configureGuiTargetDamage, guiTargetDamageStats, renderGui,
  configureGuiUniformImages, configureGuiTintCache, configureGuiScreenClipFastPath} from '../runtime/wwwroot/gui.js';

const identity = [1,0,0,1,0,0];
const command = (kind,value) => ({kind,value});
const matrix = value => command('matrix',value);
const fill = (rect=[.25,.25,100,100],color=[0,0,0,1]) => command('fill',{rect,color,rounding:0});
const image = (x=5,y=5,id=2) => command('imageBatch',{id,color:[1,1,1,1],quads:[x,y,3,3,0,0,1,1]});
const row = (y,draws=[image(5,y+5)]) => [command('clipScreen',[.25,y,100,20]),...draws,command('pop',0)];
const body = () => [fill(),...row(.25),...row(20.25),...row(40.25)];
const resources = () => new Map([[1,{generation:1,opaqueUniform:true}],[2,{generation:2,opaqueUniform:false}]]);
const prepare = (planner,commands=body(),options={}) => planner.prepare(options.id??1,options.width??128,128,
  options.matrix??identity,options.clips??[],commands,options.resources??resources(),options.generation??1);
const contains = (plan,x,y) => plan?.preserved.some(([l,t,r,b]) => x>=l && x<r && y>=t && y<b) ?? false;
const warm = (commands=body(),options={}) => {
  const planner = new GuiTargetDamagePlanner();
  assert.equal(prepare(planner,commands,options),null);
  return planner;
};

test('identical ordered rows preserve reset interiors including unchanged fractional row boundaries',()=>{
  const planner=warm(), plan=prepare(planner);
  assert.ok(contains(plan,10,10));
  assert.ok(contains(plan,10,19));assert.ok(contains(plan,10,20));
  for(const box of plan.preserved)assert.ok(box.every(Number.isInteger));
  for(const point of [[0,10],[1,10],[10,0],[10,1],[100,10],[110,110]])
    assert.equal(contains(plan,...point),false,`must replay ${point}`);
});

test('changed and erased glyphs damage both old and new footprints',()=>{
  const before=body(), planner=warm(before), after=body();
  after.splice(2,1); // First row remains as an empty, balanced clip group.
  const plan=prepare(planner,after);
  assert.equal(contains(plan,10,10),false);
  assert.ok(contains(plan,10,30));
  const moved=body();moved[1].value[1]=75;
  const movedPlan=prepare(warm(before),moved);
  assert.equal(contains(movedPlan,10,10),false);
  assert.equal(contains(movedPlan,10,80),false);
});

test('certified rows reuse only exact commands, entry transforms and live image generations',()=>{
  const planner=warm(), previous=planner.entries.get(1).groups;
  assert.ok(prepare(planner));
  assert.equal(planner.entries.get(1).groups[0],previous[0]);
  const changed=body();changed[2].value.color=[.5,1,1,1];
  prepare(planner,changed);
  const next=planner.entries.get(1).groups;
  assert.notEqual(next[0],previous[0]);assert.equal(next[1],previous[1]);
  const replaced=resources();replaced.get(2).generation++;
  prepare(planner,changed,{resources:replaced});
  assert.notEqual(planner.entries.get(1).groups[1],previous[1]);
  const signed=body();signed[2].value.quads[0]=-0;
  const invalid=structuredClone(signed);invalid[2].value.quads[0]='n:-0';
  assert.equal(prepare(warm(signed),invalid),null,'strings cannot impersonate signed numeric zero');
  const missing=resources();missing.delete(2);
  assert.equal(prepare(planner,changed,{resources:missing}),null);
  assert.equal(planner.entries.size,0);
});

test('all-row changes bypass certification without reusing a certificate across skipped writes',()=>{
  const planner=warm(), changed=body();
  for(const item of changed)if(item.kind==='imageBatch')item.value.color=[.5,1,1,1];
  // A skipped certification must not even consult resource metadata.
  const inaccessible={get(){throw Error('unexpected resource lookup');}};
  assert.equal(prepare(planner,changed,{resources:inaccessible}),null);
  assert.equal(planner.bypassed,1);
  assert.equal(prepare(planner,changed),null,'first stable frame establishes a new certificate');
  assert.ok(prepare(planner,changed),'second stable frame can reuse it');
  const before=planner.bypassed;
  assert.equal(prepare(planner),null);
  assert.equal(planner.bypassed,before+1);
});

test('removed/reordered groups and overlapping changed rows cannot leave old pixels',()=>{
  const before=body();
  const removed=before.slice(0,-3), plan=prepare(warm(before),removed);
  assert.ok(contains(plan,10,10));assert.equal(contains(plan,10,50),false);
  const reorder=[fill(),...row(20.25),...row(.25),...row(40.25)];
  const reordered=prepare(warm(before),reorder);
  assert.equal(contains(reordered,10,10),false);assert.equal(contains(reordered,10,30),false);
  assert.ok(contains(reordered,10,50));
  const overlap=[fill(),...row(.25),...row(10.25)];
  const changed=structuredClone(overlap);changed[5].value.color=[1,0,0,1];
  const overlapPlan=prepare(warm(overlap),changed);
  assert.equal(contains(overlapPlan,10,15),false);
  assert.ok(contains(overlapPlan,10,5));
});

test('exact group identity includes UVs, tint, transform, resources, geometry and draw order',()=>{
  const before=body();
  for(const mutate of [
    b=>{b[2].value.quads[4]=.125;b[2].value.quads[6]=.875;},
    b=>{b[2].value.color[0]=.5;},
    b=>{b[2].value.quads[0]=6;},
    b=>{b[2].value.id=1;},
    b=>{b.splice(2,0,matrix([1,0,0,1,1,0]));},
    b=>{b.splice(2,0,image(9,5));},
  ]) {
    const next=body();mutate(next);
    assert.equal(contains(prepare(warm(before),next),10,10),false);
  }
  const r=resources();r.get(2).generation++;
  assert.equal(contains(prepare(warm(before),before,{resources:r}),10,10),false);
  const multiple=[fill(),...row(.25,[image(5,5),image(10,5)]),...row(20.25)];
  const reordered=structuredClone(multiple);[reordered[2],reordered[3]]=[reordered[3],reordered[2]];
  assert.equal(contains(prepare(warm(multiple),reordered),10,10),false);
});

test('generation, dimensions, entry clips/transform and reset changes require a full replay',()=>{
  for(const options of [{generation:2},{width:129},{matrix:[2,0,0,2,0,0]},
    {clips:[[{x:0,y:0},{x:90,y:0},{x:90,y:90},{x:0,y:90}]]}])
    assert.equal(prepare(warm(),body(),options),null);
  const changed=body();changed[0].value.color=[1,0,0,1];assert.equal(prepare(warm(),changed),null);
  const planner=warm();planner.invalidate();assert.equal(prepare(planner),null);
  prepare(planner);planner.forget(1);assert.equal(prepare(planner),null);
});

test('positive axis transforms and inherited fractional clips restrict physical interiors',()=>{
  const options={matrix:[1.5,0,0,1.5,2.25,3.25],clips:[[{x:4.25,y:4.25},{x:90.75,y:4.25},{x:90.75,y:90.75},{x:4.25,y:90.75}]]};
  const plan=prepare(warm(body(),options),body(),options);
  assert.ok(contains(plan,10,10));assert.equal(contains(plan,5,10),false);assert.equal(contains(plan,90,10),false);
});

test('uniform opaque image resets qualify; unknown resources, translucent resets and unsupported bodies do not',()=>{
  const commands=body();commands[0]=image(.25,.25,1);commands[0].value.quads=[.25,.25,100,100,0,0,1,1];
  assert.ok(prepare(warm(commands),commands));
  for(const mutate of [
    b=>{b[0].value.color[3]=.5;},
    b=>{b[0].value.id=2;},
    b=>{b[0].value.quads.push(...b[0].value.quads);},
    b=>{b[2]=command('text',{});},
    b=>{b[2]=fill();},
    b=>{b[2]=command('targetTexture',{id:1});},
    b=>{b[2]=command('image',{id:2});},
    b=>{b[2]=command('releaseImage',2);},
    b=>{b[2].value.quads[2]=-1;},
    b=>{b[2].value.quads[0]=NaN;},
    b=>{b[0].value.quads[6]=2;},
    b=>{b.splice(3,1);},
    b=>{b.push(command('pop',0));},
    b=>{b.splice(2,0,matrix([1,1,0,1,0,0]));},
  ]) {
    const next=structuredClone(commands);mutate(next);
    const planner=warm(commands);assert.equal(prepare(planner,next),null);assert.equal(planner.entries.size,0);
  }
  const rounded=body();rounded[0].value.rounding=2;assert.equal(prepare(new GuiTargetDamagePlanner(),rounded),null);
  for(const transform of [[-1,0,0,1,0,0],[1,0,1,1,0,0],[1,0,0,0,0,0]])
    assert.equal(prepare(new GuiTargetDamagePlanner(),body(),{matrix:transform}),null);
});

test('row UVs preserve exact rounded atlas edges, signed crops and zero-area draws while reset proof stays strict',()=>{
  const cases=[
    [0,.9980469,1,.001953125], // Native float JSON extends slightly past atlas edge.
    [-.25,0,1.5,1],
    [1,1,-1,-1],
    [0,0,0,1],
  ];
  for(const uv of cases) {
    const commands=body();commands[2].value.quads.splice(4,4,...uv);
    assert.ok(contains(prepare(warm(commands),commands),10,10));
    const changed=structuredClone(commands);changed[2].value.quads[4]+=.001;
    assert.equal(contains(prepare(warm(commands),changed),10,10),false);
    const reset=structuredClone(commands);reset[0]=image(0,0,1);reset[0].value.quads=[0,0,100,100,...uv];
    assert.equal(prepare(new GuiTargetDamagePlanner(),reset),null);
  }
  for(const index of [2,3]) {
    const commands=body();commands[2].value.quads[index]=0;
    assert.ok(contains(prepare(warm(commands),commands),10,10));
  }
});

test('snapshots are detached, bounded and evict old target identities',()=>{
  const planner=new GuiTargetDamagePlanner(2), original=body();
  prepare(planner,original);original[2].value.color[0]=.125;
  assert.ok(contains(prepare(planner,body()),10,10));
  prepare(planner,body(),{id:2});prepare(planner,body(),{id:3});
  assert.equal(planner.entries.size,2);assert.ok(planner.retainedBytes<=planner.byteLimit);
  assert.equal(prepare(planner,body(),{id:1}),null);
  const tiny=new GuiTargetDamagePlanner(8,10);assert.equal(prepare(tiny),null);assert.equal(tiny.retainedBytes,0);
  planner.invalidate();assert.equal(planner.retainedBytes,0);
});

test('nested physical clips, restored matrices and numeric/packed batches keep exact identity',()=>{
  const values=[5,5,3,3,0,0,1,1],bytes=Buffer.alloc(8+values.length*10);
  bytes.writeUInt32LE(0x31514c53,0);bytes.writeUInt32LE(values.length,4);
  values.forEach((value,i)=>{bytes.writeDoubleLE(value,8+i*8);bytes.writeUInt16LE(i,8+values.length*8+i*2);});
  const commands=[fill(),command('clipScreen',[.25,.25,100,20]),
    command('clipScreen',[2.5,2.5,90,15]),matrix([1,0,0,1,1,0]),image(),command('pop',0),
    image(15,5),command('pop',0),matrix(identity),...row(20.25)];
  const packed=structuredClone(commands);packed[4].value.quads=bytes.toString('base64');
  assert.ok(contains(prepare(warm(commands),packed),10,10));
  packed[2].value[0]++;
  assert.equal(contains(prepare(warm(commands),packed),10,10),false);
  const signed=body();signed[2].value.quads[0]=0;
  const negative=structuredClone(signed);negative[2].value.quads[0]=-0;
  assert.equal(contains(prepare(warm(signed),negative),10,10),false);
});

// A stateful Canvas double verifies replay/clip ownership and draw omission,
// without asserting raster equivalence (the supervising worker owns that check).
class Context {
  constructor(canvas) {this.canvas=canvas;this.stack=[];this.transform=identity;this.calls=[];}
  resetTransform(){this.transform=identity;}
  getTransform(){const [a,b,c,d,e,f]=this.transform;return {a,b,c,d,e,f};}
  setTransform(...v){this.transform=v.length===1 ? Object.values(v[0]) : v;}
  save(){this.stack.push(this.transform);}
  restore(){this.transform=this.stack.pop();}
  beginPath(){this.path=[];}
  rect(...r){this.path.push(r);}
  clip(){this.calls.push(['clip',this.path]);}
  roundRect(...r){this.calls.push(['reset',r]);}
  fill(){}
  drawImage(...args){this.calls.push(['draw',args]);}
  putImageData(){}
}
class Canvas extends EventTarget {
  constructor(){super();this.width=128;this.height=128;this.context=new Context(this);}
  getContext(){return this.context;}
}
test('Canvas integration keeps state/reset order, culls interior images and invalidates settings/lifetimes/errors',()=>{
  const oldDocument=globalThis.document,oldImageData=globalThis.ImageData,oldDOMPoint=globalThis.DOMPoint;
  const canvases=[];
  globalThis.document={createElement(){const canvas=new Canvas();canvases.push(canvas);return canvas;}};
  globalThis.ImageData=class {constructor(data,width,height){Object.assign(this,{data,width,height});}};
  globalThis.DOMPoint=class {
    constructor(x,y){Object.assign(this,{x,y});}
    matrixTransform(m){return {x:this.x*m.a+this.y*m.c+m.e,y:this.x*m.b+this.y*m.d+m.f};}
  };
  const screen=new Canvas(), open=command('target',{id:1234,width:128,height:128}),close=command('target',{id:0});
  const packet=()=>[open,...body(),close];
  const upload=()=>command('image',{id:2,width:1,height:1,pixels:Buffer.from([255,255,255,255]).toString('base64')});
  const previous=configureGuiTargetDamage(true);assert.equal(previous,false);
  try {
    renderGui(screen,[upload()]);renderGui(screen,packet());renderGui(screen,packet());
    assert.equal(guiTargetDamageStats().planned,1);assert.ok(guiTargetDamageStats().preservedPixels>0);
    assert.ok(guiTargetDamageStats().skippedDraws>0);
    const target=canvases[1];assert.equal(target.context.stack.length,0);
    const scratch=canvases[2];
    assert.ok(scratch.context.calls.some(([kind,args])=>kind==='draw' && args[0]===target));
    const restored=target.context.calls.filter(([kind,args])=>kind==='draw' && args[0]===scratch);
    assert.ok(restored.length);assert.ok(restored.every(([,args])=>args.slice(1).every(Number.isInteger)));
    assert.equal(target.context.calls.some(([kind,path])=>kind==='clip' && path.some(r=>r[2]<0)),false);
    assert.equal(target.context.calls.filter(([kind])=>kind==='reset').length,2);
    scratch.context.isContextLost=()=>true;
    const beforeLost=guiTargetDamageStats().planned;
    renderGui(screen,packet());assert.equal(guiTargetDamageStats().planned,beforeLost);
    assert.equal(guiTargetDamageStats().scratchBytes,0);
    renderGui(screen,packet());assert.equal(guiTargetDamageStats().planned,beforeLost);
    renderGui(screen,packet());assert.equal(guiTargetDamageStats().planned,beforeLost+1);
    const fullThenPlan = invalidate => {
      const planned=guiTargetDamageStats().planned;invalidate();renderGui(screen,packet());
      assert.equal(guiTargetDamageStats().planned,planned);
      renderGui(screen,packet());assert.equal(guiTargetDamageStats().planned,planned+1);
    };
    fullThenPlan(()=>target.dispatchEvent(new Event('contextlost')));
    fullThenPlan(()=>screen.dispatchEvent(new Event('contextrestored')));
    fullThenPlan(()=>{screen.width++;});
    fullThenPlan(()=>renderGui(screen,[upload()]));
    fullThenPlan(()=>renderGui(screen,[command('releaseTarget',1234)]));
    for(const configure of [configureGuiUniformImages,configureGuiTintCache,configureGuiScreenClipFastPath]) {
      fullThenPlan(()=>configure(false));configure(true);
      renderGui(screen,packet());renderGui(screen,packet());
    }
    fullThenPlan(()=>assert.throws(()=>renderGui(screen,[command('unknown',null)]),/Unknown/));
    const stats=guiTargetDamageStats();stats.planned=-1;assert.notEqual(guiTargetDamageStats().planned,-1);
    assert.equal(configureGuiTargetDamage(false),true);assert.equal(guiTargetDamageStats().snapshots,0);
    assert.equal(guiTargetDamageStats().scratchBytes,0);
    renderGui(screen,packet());assert.equal(guiTargetDamageStats().writes,0);
  } finally {
    configureGuiTargetDamage(false);globalThis.document=oldDocument;globalThis.ImageData=oldImageData;globalThis.DOMPoint=oldDOMPoint;
  }
});
