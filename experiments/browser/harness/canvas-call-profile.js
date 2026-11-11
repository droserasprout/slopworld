// Opt-in diagnostic only: method timing includes instrumentation overhead and
// measures synchronous submission, not GPU completion. Source summaries retain
// dimensions and a uniform texel's RGBA only, never nonuniform image contents.
(async() => {
  const prototype=CanvasRenderingContext2D.prototype;
  const screen=document.querySelector('canvas'),originals=new Map();
  const sourceDescriptions=new WeakMap();
  let rows={};
  for(const method of ['drawImage','clearRect','fill','fillRect','stroke','clip','save','restore',
    'beginPath','rect','roundRect','moveTo','lineTo','closePath','setTransform','resetTransform','getTransform',
    'translate','getImageData','putImageData','fillText']) {
    const original=prototype[method];if(typeof original!=='function')continue;
    originals.set(method,original);
    prototype[method]=function(...args) {
      let suffix='',dimensions;
      if(method==='drawImage') {
        const source=args[0],area=source.width*source.height;
        const width=args.length===9?args[7]:args.length===5?args[3]:source.width;
        const height=args.length===9?args[8]:args.length===5?args[4]:source.height;
        suffix='.'+(area>1000000?'large':area>65536?'medium':'small')+
          '.'+(source instanceof ImageBitmap?'bitmap':'canvas')+
          '.'+(Math.abs(width*height)>1000000?'largeDestination':'smallDestination');
        dimensions=[source.width,source.height,width,height];
      }
      const key=(this.canvas===screen?'screen.':'target.')+method+suffix;
      const row=rows[key]??={count:0,ms:0,maxMs:0};
      if(dimensions) {
        row.dimensions??=[];
        if(row.dimensions.length<8 && !row.dimensions.some(d=>d.every((v,i)=>v===dimensions[i])))row.dimensions.push(dimensions);
        if(key.includes('small.canvas.largeDestination')) {
          row.alphas??=[];
          if(row.alphas.length<8 && !row.alphas.includes(this.globalAlpha))row.alphas.push(this.globalAlpha);
          const source=args[0];let description=sourceDescriptions.get(source);
          if(!description && source instanceof HTMLCanvasElement) {
            const data=Reflect.apply(originals.get('getImageData'),source.getContext('2d'),[0,0,source.width,source.height]).data;
            const uniform=data.every((v,i)=>v===data[i%4]);
            description={width:source.width,height:source.height,uniform,rgba:uniform?Array.from(data.slice(0,4)):null};
            sourceDescriptions.set(source,description);
          }
          row.sources??=[];
          if(description && row.sources.length<8 && !row.sources.some(d=>JSON.stringify(d)===JSON.stringify(description)))row.sources.push(description);
        }
      }
      const start=performance.now();
      try{return Reflect.apply(original,this,args);}
      finally {const ms=performance.now()-start;row.count++;row.ms+=ms;row.maxMs=Math.max(row.maxMs,ms);}
    };
  }
  const {GuiFrameReuse}=await import('/gui-frame-reuse.js');
  const prepare=GuiFrameReuse.prototype.prepare;
  GuiFrameReuse.prototype.prepare=function(...args) {
    const row=rows['reuse.prepare']??={count:0,ms:0,maxMs:0};
    const start=performance.now();
    try{return Reflect.apply(prepare,this,args);}
    finally {const ms=performance.now()-start;row.count++;row.ms+=ms;row.maxMs=Math.max(row.maxMs,ms);}
  };
  window.__canvasCallProfile={
    reset(){rows={};},
    take(){return {measurement:'Instrumented synchronous Canvas method calls; timing overhead included',rows};},
    restore(){
      for(const [key,value] of originals)prototype[key]=value;
      GuiFrameReuse.prototype.prepare=prepare;delete window.__canvasCallProfile;
    },
  };
})();
