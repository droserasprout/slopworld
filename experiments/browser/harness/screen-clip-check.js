// Real Canvas byte equivalence; this is a correctness check, not a benchmark.
import {renderGui,configureGuiScreenClipFastPath,guiTargetCount} from './gui.js';

const command=(kind,value=null)=>({kind,value});
const fill=(color,rect=[-100,-100,400,400])=>command('fill',{rect,color,rounding:0});
const pop=()=>command('pop');
const matrix=command('matrix',[1.1,.25,-.15,.9,7.5,3.25]);
const pixels=canvas=>canvas.getContext('2d').getImageData(0,0,canvas.width,canvas.height).data;
let nextTarget=91000;

// Balance local clips before changing targets; outer screen clips stay live
// throughout the transitions and are inherited by every target entry.
function fixture(rect,targets) {
  const ids=[++nextTarget,++nextTarget];
  const commands=[matrix,command('group',{rect:[3.5,2.25,100,78],offset:[1.25,-2.5]}),
    command('clipScreen',rect),fill([.8,.2,.1,.7]),
    command('clip',[-8,4,68,61]),command('clipScreen',[10.25,6.5,72.5,60.25]),fill([.1,.7,.4,.65])];
  if(targets) {
    commands.push(command('target',{id:ids[0],width:128,height:96}),
      fill([.6,.1,.8,.8]),command('clipScreen',[18.5,13.25,38.25,44.5]),
      command('group',{rect:[4,8,60,52],offset:[-3,2]}),fill([.1,.5,.9,.9]),pop(),pop(),
      command('target',{id:ids[1],width:128,height:96}),fill([.9,.7,.1,.75]),
      command('clip',[2.25,5.5,47.25,38.5]),fill([.1,.9,.8,1]),pop(),
      command('target',{id:0}),fill([.3,.1,.7,.3]),
      // Reenter a retained target, then leave with the inherited stack intact.
      command('target',{id:ids[0],width:128,height:96}),fill([.5,.8,.1,.4]),command('target',{id:0}));
  }
  commands.push(pop(),pop(),fill([.8,.4,.2,.25]),pop(),pop(),command('matrix',[1,0,0,1,0,0]));
  if(targets) {
    // Reveal every inherited target pixel after the screen stack is popped.
    for(const id of ids)commands.push(command('targetTexture',{id,color:[1,1,1,.7],uv:[0,0,1,1],rect:[0,0,128,96]}),command('releaseTarget',id));
  }
  // Verify all pops restore the screen clip, even for empty intersections.
  commands.push(fill([1,1,1,1],[119,87,4,4]));
  return commands;
}

try {
  const make=()=>{const canvas=document.createElement('canvas');canvas.width=128;canvas.height=96;return canvas;};
  const previous=configureGuiScreenClipFastPath(false);
  if(previous!==false)throw new Error('Screen clip fast path must default off');
  const cases=[[8,7,87,65],[94.5,76.25,-80.25,-62.5],[-8.75,-2.5,66.25,57.75],
    [18,12,0,45],[18,12,45,0],[18,12,0,0],[200,180,20,20],[9.125,7.875,.5,.25]];
  let scenarios=0;
  try {
    for(const rect of cases)for(const targets of [false,true]) {
      const reference=make(),actual=make();
      configureGuiScreenClipFastPath(false);renderGui(reference,fixture(rect,targets));
      if(configureGuiScreenClipFastPath(true)!==false)throw new Error('Switch did not return prior disabled mode');
      renderGui(actual,fixture(rect,targets));
      const a=pixels(actual),b=pixels(reference);
      const mismatch=a.findIndex((value,index)=>value!==b[index]);
      if(mismatch!==-1)throw new Error(`Clip ${rect}, targets=${targets}: byte ${mismatch} differs (${a[mismatch]} vs ${b[mismatch]})`);
      if(a[(88*128+120)*4+3]!==255)throw new Error('Popped clip still constrained screen drawing');
      // Ordinary intersections must render real content as well as the pop sentinel.
      if(rect===cases[0]&&!a.some((value,index)=>index%4===3&&index<(80*128)*4&&value>0))throw new Error('Fixture did not draw inside clip');
      if(guiTargetCount()!==0)throw new Error('Fixture leaked a render target');
      scenarios++;
    }
    if(configureGuiScreenClipFastPath(false)!==true)throw new Error('Switch did not return prior enabled mode');
    window.__probeResult={status:'ok',pixelExact:true,scenarios};
  } finally {configureGuiScreenClipFastPath(previous);}
} catch(error) {window.__probeResult={status:'fail',error:String(error)};}
