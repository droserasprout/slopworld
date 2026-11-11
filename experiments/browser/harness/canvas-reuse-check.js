import {attachGuiInput} from './gui-input.js';
import {renderGui} from './gui.js';

try {
  const make=()=>{const canvas=document.createElement('canvas');canvas.width=128;canvas.height=96;document.body.append(canvas);return canvas;};
  const actual=make(),reference=make(),samples=[];
  let updates=0,clipboard=0,enabled=true;
  const image=red=>[{kind:'image',value:{id:901,width:1,height:1,pixels:btoa(String.fromCharCode(red,80,150,255))}}];
  let commands=[
    {kind:'fill',value:{rect:[0,0,128,96],color:[.1,.2,.3,1],rounding:0}},
    {kind:'clipScreen',value:[12,9,90,65]},
    {kind:'matrix',value:[1.25,0,0,1.25,4,3]},
    {kind:'imageBatch',value:{id:901,color:[1,1,1,.65],quads:[0,0,60,60,0,0,1,1,30,20,60,60,0,0,1,1]}},
    {kind:'pop',value:null},
  ];
  const input=attachGuiInput(actual,{
    get allowFrameReuse(){return enabled;},
    GuiInputFrame(){updates++;return JSON.stringify({commands});},
    ClipboardTake(){clipboard++;return '[]';},
    profileFrame:sample=>samples.push(sample),
  });
  const pixels=canvas=>canvas.getContext('2d').getImageData(0,0,canvas.width,canvas.height).data;
  const check=label=>{
    const a=pixels(actual),b=pixels(reference);
    if(a.length!==b.length||a.some((value,index)=>value!==b[index]))throw new Error(label+': pixels differ');
  };
  const paintReference=()=>{reference.getContext('2d').clearRect(0,0,reference.width,reference.height);renderGui(reference,structuredClone(commands));};
  const expectReuse=value=>{if(samples.at(-1).canvasReused!==value)throw new Error('Unexpected reuse decision');};
  try {
    renderGui(reference,image(200));paintReference();
    input.frame();input.frame();expectReuse(1);check('unchanged transformed alpha image');
    commands[0].value.color=[.8,.2,.1,1];
    input.frame();expectReuse(0);paintReference();check('changed color');
    renderGui(reference,image(10));
    input.frame();expectReuse(0);paintReference();check('external resource update');
    actual.width=reference.width=140;
    input.frame();expectReuse(0);paintReference();check('resized surface');
    enabled=false;input.frame();input.frame();expectReuse(0);check('dynamic background admission');
    if(updates!==7||clipboard!==7)throw new Error('Reuse skipped native or clipboard processing');
    window.__probeResult={status:'ok',pixelExact:true,updates,clipboard,scenarios:5};
  } finally {input.dispose();}
} catch(error) {window.__probeResult={status:'fail',error:String(error)};}
