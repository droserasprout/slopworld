import { renderGui, guiTargetCount, guiImageCount } from './gui.js';

export function verifyGuiBatching() {
  const imageId=-2147483000;
  const a=document.createElement('canvas'), b=document.createElement('canvas');
  a.width=b.width=80; a.height=b.height=60;
  const pixels=new Uint8Array([255,255,255,255, 255,0,0,128, 0,255,0,64, 0,0,255,255]);
  const upload={kind:'image',value:{id:imageId,width:2,height:2,pixels:btoa(String.fromCharCode(...pixels))}};
  const quads=[[2,3,19,21,0,0,1,1], [8,9,22,17,.5,0,.5,1], [-3,15,14,11,0,.5,1,.5]];
  const color=[.5,.75,.25,.6];
  const prefix=[upload, {kind:'matrix',value:[1.5,0,0,1.5,2,3]}, {kind:'clipScreen',value:[5,4,45,34]}];
  try {
    renderGui(a,[...prefix,...quads.map(q=>({kind:'imageTexture',value:{id:imageId,color,rect:q.slice(0,4),uv:q.slice(4)}})),{kind:'pop',value:0}]);
    renderGui(b,[...prefix,{kind:'imageBatch',value:{id:imageId,color,quads:quads.flat()}},{kind:'pop',value:0}]);
    const expected=a.getContext('2d').getImageData(0,0,80,60).data;
    const actual=b.getContext('2d').getImageData(0,0,80,60).data;
    if (actual.some((value,index)=>value!==expected[index]))
      throw new Error('Batched glyphs changed transformed/clipped/overlapping alpha pixels');
    return {byteIdentical:true,pixels:80*60};
  } finally {renderGui(a,[{kind:'releaseImage',value:imageId}]);}
}

export function verifyGuiTargets(managed) {
  const canvas = document.createElement('canvas'); canvas.width = 40; canvas.height = 40;
  const ctx = canvas.getContext('2d'), before = guiTargetCount();
  const imagesBefore = guiImageCount();
  const pixel = (x,y) => Array.from(ctx.getImageData(x,y,1,1).data).join(',');
  for (let stage = 0; stage < 4; stage++) {
    const commands = JSON.parse(managed.GuiTargetFrame(stage));
    ctx.clearRect(0,0,40,40); renderGui(canvas, commands);
    if (stage < 2 && (pixel(5,5) !== '0,0,255,255' || pixel(25,5) !== '255,0,0,255' || pixel(5,25) !== '255,0,0,255'))
      throw new Error('Cached target lost its clip, pixels, or UV orientation');
    if (stage === 1 && commands.some(c => c.kind === 'target')) throw new Error('Cache hit unexpectedly repainted the target');
    if (stage === 1 && commands.some(c => c.kind === 'image')) throw new Error('Unchanged image was uploaded twice');
    if (stage < 3 && pixel(38,5) !== (stage === 2 ? '0,255,0,255' : '255,0,0,255')) throw new Error('Image Apply did not invalidate uploaded pixels');
    if (stage === 2 && pixel(5,5) !== '0,255,0,255') throw new Error('Recreated target retained stale pixels');
  }
  if (guiTargetCount() !== before) throw new Error('Released GUI target was retained');
  if (guiImageCount() !== imagesBefore) throw new Error('Disposed GUI image was retained');
  return { retained: true, recreated: true, released: true, batching:verifyGuiBatching() };
}
