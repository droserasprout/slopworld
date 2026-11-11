// Observe one real frame without consuming resource commands or sending input.
(async () => {
  const canvas=document.querySelector('canvas'), context=canvas.getContext('2d');
  const x=500,y=1500;
  if(canvas.width!==3072||canvas.height!==1664)throw new Error('Expected fixed reference geometry');
  const original=context.drawImage, steps=[];
  const pixel=()=>Array.from(context.getImageData(x,y,1,1).data);
  let calls=0;
  try {
    context.drawImage=function(source,...args) {
      const before=pixel();
      original.call(this,source,...args);
      const after=pixel(); calls++;
      if(before.some((v,i)=>v!==after[i])&&steps.length<20) {
        const transform=this.getTransform();
        steps.push({before,after,alpha:this.globalAlpha,sourceSize:[source.width,source.height],
          sourcePixel:source.width<=64&&source.height<=64?Array.from(source.getContext('2d').getImageData(Math.floor(source.width/2),Math.floor(source.height/2),1,1).data):null,
          args,transform:[transform.a,transform.b,transform.c,transform.d,transform.e,transform.f]});
      }
    };
    const frame=window.__workspace.frames;
    const until=performance.now()+5000;
    while(window.__workspace.frames===frame&&performance.now()<until)await new Promise(r=>setTimeout(r,10));
    if(!calls)throw new Error('No real drawing observed');
    return {point:[x,y],calls,steps};
  } finally { context.drawImage=original; }
})()
