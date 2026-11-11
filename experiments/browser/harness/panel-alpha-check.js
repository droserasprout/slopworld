(() => {
  const source=document.createElement('canvas');source.width=source.height=2;
  source.getContext('2d').fillStyle='rgb(43,32,20)';source.getContext('2d').fillRect(0,0,2,2);
  return ['unorm8','float16'].flatMap(colorType=>{
  const canvas=document.createElement('canvas');canvas.width=canvas.height=2;
  const context=canvas.getContext('2d',{colorType});
  return ['image','fill','bytes'].flatMap(kind=>[.9607843,245/255].map(alpha=>{
    context.globalAlpha=1;context.fillStyle='rgb(20,18,14)';context.fillRect(0,0,2,2);
    context.globalAlpha=alpha;
    if(kind==='fill'){context.fillStyle='rgb(43,32,20)';context.fillRect(0,0,2,2);}
    else {
      if(kind==='bytes')source.getContext('2d').putImageData(new ImageData(new Uint8ClampedArray([43,32,20,255,43,32,20,255,43,32,20,255,43,32,20,255]),2,2),0,0);
      context.drawImage(source,0,0);
    }
    return {kind,alpha,settings:context.getContextAttributes(),pixel:Array.from(context.getImageData(0,0,1,1).data)};
  }));});
})()
