// One pending frame or idle timer. Activity cancels waiting; native drawing still
// decides whether the next frame is unchanged. Never infer stability from time alone.
export class WorkspaceScheduler {
  constructor(draw, {raf=callback=>requestAnimationFrame(callback),cancelRaf=id=>cancelAnimationFrame(id),
    timer=(callback,delay)=>setTimeout(callback,delay),cancelTimer=id=>clearTimeout(id),now=()=>performance.now(),deadline=()=>100}={}) {
    Object.assign(this,{draw,raf,cancelRaf,timer,cancelTimer,now,deadline});
    this.frame=null;this.wait=null;this.stable=0;this.holdUntil=0;
    this.enabled=false;this.stopped=false;
  }
  setEnabled(enabled) {const previous=this.enabled;this.enabled=enabled;this.wake();return previous;}
  wake(interactive=false) {
    if(this.stopped)return;
    this.stable=0;
    if(interactive)this.holdUntil=this.now()+500;
    if(this.wait!==null){this.cancelTimer(this.wait);this.wait=null;}
    this.request();
  }
  request() {
    if(this.stopped||this.frame!==null)return;
    this.frame=this.raf(()=>{
      this.frame=null;
      const started=this.now();
      const unchanged=this.draw();
      if(this.stopped)return;
      this.stable=unchanged?this.stable+1:0;
      if(this.frame!==null)return; // Activity published during native update already requested work.
      if(this.enabled&&this.stable>=3&&this.now()>=this.holdUntil) {
        // Native owners report seconds-to-work at the completed frame. Reserve
        // one refresh interval for RAF; generic timers are serviced within 100 ms.
        // Deadline hints are optional: failures fall back to full-rate updates.
        let due=0;
        try { due=this.deadline(); } catch { /* Keep servicing native timers. */ }
        const budget=Number.isFinite(due)?Math.max(0,Math.min(100,due-(this.now()-started))):0;
        const interval=Math.min(32*2**Math.min(2,this.stable-3),budget);
        if(interval<=16)this.request();
        else this.wait=this.timer(()=>{this.wait=null;this.request();},interval-16);
      } else this.request();
    });
  }
  stop() {
    this.stopped=true;
    if(this.frame!==null)this.cancelRaf(this.frame);
    if(this.wait!==null)this.cancelTimer(this.wait);
    this.frame=this.wait=null;
  }
}
