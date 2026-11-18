// Resolved I/O promises do not yield to rendering. Bound consecutive startup
// work between paint opportunities; individual managed calls remain synchronous.
let lastPaint=0;
export async function yieldStartupPaint(force=false) {
  if(typeof globalThis.requestAnimationFrame!=='function' || (!force && performance.now()-lastPaint<8))return;
  await new Promise(resolve=>{
    let settled=false;
    const finish=()=>{
      if(settled)return;
      settled=true;clearTimeout(timer);cancelAnimationFrame(frame);resolve();
    };
    // Hidden tabs may suspend RAF. A timer also keeps startup moving there.
    const timer=setTimeout(finish,100);
    const frame=requestAnimationFrame(()=>setTimeout(finish,0));
  });
  lastPaint=performance.now();
}
