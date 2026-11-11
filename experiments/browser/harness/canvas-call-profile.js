// Opt-in diagnostic only: method timing includes instrumentation overhead and
// measures synchronous submission, not GPU completion. Never record image data.
(async() => {
  const prototype=CanvasRenderingContext2D.prototype;
  const screen=document.querySelector('canvas'),originals=new Map();
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
