// Compare immutable upload sources through real Firefox Canvas glyph playback.
// No game, WASM, daemon sessions, screenshots or desktop windows are involved.
async function bitmapPaintCheck() {
  const make=(w,h)=>Object.assign(document.createElement('canvas'),{width:w,height:h});
  const read=c=>c.getContext('2d').getImageData(0,0,c.width,c.height).data;
  const median=a=>a.toSorted((a,b)=>a-b)[Math.floor(a.length/2)];
  const results=[];
  for(const side of [256,1024,2048]) {
    const atlas=make(side,side), pixels=new Uint8ClampedArray(side*side*4);
    for(let i=0;i<pixels.length;i+=4) {
      pixels[i]=173;pixels[i+1]=211;pixels[i+2]=239;
      pixels[i+3]=(i*73+(i>>8))%256;
    }
    atlas.getContext('2d').putImageData(new ImageData(pixels,side,side),0,0);
    const started=performance.now(), bitmap=await createImageBitmap(atlas);
    const promotionMs=performance.now()-started;
    try {
      for(const fractional of [false,true]) {
        const surfaces=[make(1800,1000),make(1800,1000)], samples={canvas:[],bitmap:[]};
        const paint=(source,index)=>{
          const ctx=surfaces[index].getContext('2d');ctx.clearRect(0,0,1800,1000);
          ctx.save();ctx.beginPath();ctx.rect(13.3,7.7,1750,975);ctx.clip();
          ctx.globalAlpha=.93;
          for(let i=0;i<7000;i++) {
            const sx=(i*17)%(side-24),sy=(i*31)%(side-40);
            ctx.drawImage(source,sx,sy,24,40,13+(i%125)*14+(fractional?.33:0),
              8+Math.floor(i/125)*17, fractional?14.4:14,fractional?24.2:24);
          }
          ctx.restore();
        };
        for(let round=0;round<10;round++) {
          for(const mode of round%2?['bitmap','canvas']:['canvas','bitmap']) {
            const index=mode==='canvas'?0:1,source=index?bitmap:atlas;
            const start=performance.now();paint(source,index);
            const submitted=performance.now()-start;
            // Separately record readback to catch deferred Canvas playback.
            const beforeRead=performance.now();read(surfaces[index]);
            samples[mode].push({submitMs:submitted,readMs:performance.now()-beforeRead});
          }
          await new Promise(r=>setTimeout(r,0));
        }
        const a=read(surfaces[0]),b=read(surfaces[1]);let differingBytes=0,maxDifference=0;
        for(let i=0;i<a.length;i++)if(a[i]!==b[i]){differingBytes++;maxDifference=Math.max(maxDifference,Math.abs(a[i]-b[i]));}
        results.push({side,fractional,glyphs:7000,promotionMs,differingBytes,maxDifference,
          medians:Object.fromEntries(Object.entries(samples).map(([k,v])=>[k,{
            submitMs:median(v.map(x=>x.submitMs)),readMs:median(v.map(x=>x.readMs)),
          }])),samples});
      }
    } finally {bitmap.close();}
  }
  return {userAgent:navigator.userAgent,results};
}
