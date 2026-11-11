// One pending frame or yield timer. Activity cancels waiting; native drawing still
// decides whether the next frame is unchanged. Never infer stability from time alone.
export class WorkspaceScheduler {
  constructor(draw, {raf=callback=>requestAnimationFrame(callback),cancelRaf=id=>cancelAnimationFrame(id),
    timer=(callback,delay)=>setTimeout(callback,delay),cancelTimer=id=>clearTimeout(id),now=()=>performance.now()}={}) {
    Object.assign(this,{draw,raf,cancelRaf,timer,cancelTimer,now});
    this.frame=null;this.wait=null;this.stable=0;this.holdUntil=0;
    this.enabled=false;this.taskYield=false;this.stopped=false;
  }
  setEnabled(enabled) {const previous=this.enabled;this.enabled=enabled;this.wake();return previous;}
  setTaskYield(enabled) {const previous=this.taskYield;this.taskYield=enabled;this.wake();return previous;}
  wake(interactive=false) {
    if(this.stopped)return;
    this.stable=0;
    if(interactive)this.holdUntil=this.now()+500;
    if(this.wait!==null){this.cancelTimer(this.wait);this.wait=null;}
    this.request();
  }
  request() {
    if(this.stopped||this.frame!==null||this.wait!==null)return;
    this.frame=this.raf(()=>{
      this.frame=null;
      const unchanged=this.draw();
      if(this.stopped)return;
      this.stable=unchanged?this.stable+1:0;
      if(this.frame!==null)return; // Activity published during native update already requested work.
      if(this.enabled&&this.stable>=3&&this.now()>=this.holdUntil) {
        this.defer(16);
      } else if(this.taskYield) {
        // Diagnostic task turn between expensive draws; activity may bypass it.
        this.defer(0);
      } else this.request();
    });
  }
  defer(delay) {
    this.wait=this.timer(()=>{this.wait=null;this.request();},delay);
  }
  stop() {
    this.stopped=true;
    if(this.frame!==null)this.cancelRaf(this.frame);
    if(this.wait!==null)this.cancelTimer(this.wait);
    this.frame=this.wait=null;
  }
}
