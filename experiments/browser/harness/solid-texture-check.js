// Actual Firefox Canvas byte comparisons for the production solid-upload path.
import {renderGui,configureGuiUniformImages,guiImageCount} from './gui.js';
const c=(kind,value)=>({kind,value});
const read=canvas=>canvas.getContext('2d').getImageData(0,0,canvas.width,canvas.height).data;
const make=()=>Object.assign(document.createElement('canvas'),{width:128,height:96});
const encode=data=>btoa(String.fromCharCode(...data));
try {
  let scenarios=0,collapsedDraws=0,optimized=false;
  const originalDraw=CanvasRenderingContext2D.prototype.drawImage;
  CanvasRenderingContext2D.prototype.drawImage=function(...args){if(optimized && args[0].width===1 && args[0].height===1)collapsedDraws++;return Reflect.apply(originalDraw,this,args);};
  const before=guiImageCount();
  try {
    for(const rgba of [[255,255,255,255],[73,121,211,255],[73,121,211,127],[0,0,0,0],[73,121,211,255,1]])
      for(const tint of [[1,1,1,1],[.31,.7,1,.37],[1,1,1,.01],[.31,.7,1,1],[1,1,1,.5],[1,1,1,1/255]])
      for(const matrix of [[1,0,0,1,0,0],[1.25,0,0,1.25,.5,.5],[1,0,0,1,.3,.7],[-1,0,0,1,128,0],[1,.2,.3,1,0,0]])
      for(const clip of [[3,2,120,90],[3.25,2.75,119.5,89.25]])
      for(const kind of ['imageTexture','imageBatch'])
      for(const uv of [[0,0,1,1],[.25,.25,.5,.5],[-.25,0,1,1],[0,0,1.5,1],[0,0,-1,1],[0,0,0,1]]) {
        const id=98701,pixels=Uint8Array.from({length:16},(_,i)=>rgba[i%4]);
        if(rgba.length===5)pixels[4]=1; // Reject a source differing after its first texel.
        const commands=[c('image',{id,width:2,height:2,pixels:encode(pixels)}),
          c('fill',{rect:[0,0,128,96],rounding:0,color:[.2,.3,.5,.7]}),c('matrix',matrix),
          c('clipScreen',clip),
          c(kind,kind==='imageBatch'?{id,color:tint,quads:[2,2,96,64,...uv]}:{id,color:tint,uv,rect:[2,2,96,64]}),c('pop'),
          c('releaseImage',id)];
        const a=make(),b=make();
        optimized=false;configureGuiUniformImages(false);renderGui(a,commands);
        optimized=true;configureGuiUniformImages(true);renderGui(b,commands);
        const expected=read(a),actual=read(b),bad=actual.findIndex((v,i)=>v!==expected[i]);
        if(bad>=0)throw Error(`Pixel mismatch ${JSON.stringify({rgba,tint,matrix,uv,clip,bad,actual:actual[bad],expected:expected[bad]})}`);
        scenarios++;
      }
    for(const [width,height] of [[1,1],[3,5],[16,16],[17,16]])
      for(const uv of [[.1,.2,.7,.6],[1/3,0,.001,1],[.9,.9,.2,.2],[0,0,-1,-1]])
      for(const rect of [[-2.7,3.1,96.3,64.2],[98.2,70.4,-80.3,-59.2]]) {
        const id=98703,pixels=Uint8Array.from({length:width*height*4},(_,i)=>[73,121,211,127][i%4]);
        const commands=[c('image',{id,width,height,pixels:encode(pixels)}),
          c('fill',{rect:[0,0,128,96],rounding:0,color:[.1,.3,.7,.37]}),
          c('matrix',[1.1,.2,-.3,.9,10.3,2.7]),c('clipScreen',[1.3,2.7,125,91]),
          c('imageTexture',{id,color:[.31,.7,1,.47],uv,rect}),c('pop'),c('releaseImage',id)];
        const a=make(),b=make();
        optimized=false;configureGuiUniformImages(false);renderGui(a,commands);
        optimized=true;configureGuiUniformImages(true);renderGui(b,commands);
        const expected=read(a),actual=read(b),bad=actual.findIndex((v,i)=>v!==expected[i]);
        if(bad>=0)throw Error(`Crop/size mismatch ${JSON.stringify({width,height,uv,rect,bad,expected:expected[bad],actual:actual[bad]})}`);
        scenarios++;
      }
    if(!collapsedDraws)throw Error('No fixture exercised collapsed uniform sources');
    {
      const id=98704,uniform=encode(Array(16).fill(255)),changed=encode([255,0,0,255,0,255,0,255,0,0,255,255,255,255,0,255]);
      const commands=[c('image',{id,width:2,height:2,pixels:uniform}),
        c('imageTexture',{id,color:[1,1,1,1],uv:[0,0,1,1],rect:[0,0,32,32]}),
        c('image',{id,width:2,height:2,pixels:changed}),
        c('imageTexture',{id,color:[1,1,1,1],uv:[0,0,1,1],rect:[32,0,32,32]}),
        c('releaseImage',id),c('releaseImage',id)];
      const a=make(),b=make();
      configureGuiUniformImages(false);renderGui(a,commands);
      configureGuiUniformImages(true);renderGui(b,commands);
      const expected=read(a),actual=read(b);
      if(actual.some((v,i)=>v!==expected[i]))throw Error('Replacement inherited retired uniform backing');
      scenarios++;
    }
    if(guiImageCount()!==before)throw Error('Leaked image');
    const benchmarks=[];
    for(const side of [1,2,4,16]) {
    const surfaces=[make(),make()],samples={reference:[],optimized:[]},id=98702;
    for(const canvas of surfaces){canvas.width=1800;canvas.height=1000;}
    renderGui(surfaces[0],[c('image',{id,width:side,height:side,pixels:encode(Array(side*side*4).fill(255))})]);
    const commands=Array.from({length:3},()=>c('imageTexture',{id,color:[.2,.3,.5,.37],uv:[0,0,1,1],rect:[0,0,1800,1000]}));
    try {
      for(let round=0;round<9;round++)for(const enabled of round%2?[true,false]:[false,true]) {
        const canvas=surfaces[enabled?1:0];configureGuiUniformImages(enabled);
        canvas.getContext('2d').clearRect(0,0,1800,1000);
        const start=performance.now();renderGui(canvas,commands);
        samples[enabled?'optimized':'reference'].push(performance.now()-start);
      }
      const a=read(surfaces[0]),b=read(surfaces[1]);
      if(a.some((v,i)=>v!==b[i]))throw Error('Large-area benchmark pixels differ');
    } finally {renderGui(surfaces[0],[c('releaseImage',id)]);}
    benchmarks.push({source:[side,side],destination:[1800,1000],drawsPerFrame:3,samples,
      medianMs:Object.fromEntries(Object.entries(samples).map(([k,v])=>[k,v.toSorted((a,b)=>a-b)[4]]))});
    }
    window.__probeResult={status:'ok',pixelExact:true,scenarios,collapsedDraws,benchmarks};
  } finally {
    CanvasRenderingContext2D.prototype.drawImage=originalDraw;
    configureGuiUniformImages(false);
  }
} catch(error){window.__probeResult={status:'fail',error:String(error)};}
