(async () => {
  const original=document.querySelector('canvas:not(#loading-screen)');
  document.getElementById('loading-screen')?.remove();
  const {startLoading}=await import('./loading.js?check='+Date.now());
  const loading=await startLoading(new URLSearchParams(location.search));
  try {
    const canvas=document.getElementById('loading-screen');
    const deadline=performance.now()+10000;
    let lit=0;
    while (!lit) {
      const pixels=canvas.getContext('2d').getImageData(0,0,canvas.width,canvas.height).data;
      for(let i=0;i<pixels.length;i+=4) if(pixels[i] || pixels[i+1] || pixels[i+2]) lit++;
      if(performance.now()>deadline) throw new Error('Loading atlas did not paint');
      if(!lit) await new Promise(resolve=>setTimeout(resolve,50));
    }
    loading.status('Loading game assemblies...');
    if(canvas.getAttribute('aria-label')!=='Loading game assemblies...')
      throw new Error('Loading status did not update');
    return {atlasPainted:true,litPixels:lit,width:canvas.width,height:canvas.height};
  } finally {
    loading.finish();
    if(document.getElementById('loading-screen') || !original.isConnected)
      throw new Error('Loading screen did not hand back the workspace');
  }
})()
