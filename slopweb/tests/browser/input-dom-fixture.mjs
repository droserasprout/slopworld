// Minimal event/focus model for adapter contracts; no browser behavior is implied.
export function installInputDom() {
  const document = Object.assign(new EventTarget(), {activeElement:null, hidden:false, body:{append(){}}});
  class Element extends EventTarget {
    constructor() { super(); this.dataset={}; this.style={}; this.value=''; this.removed=false; }
    setAttribute() {}
    after(element) { this.sibling=element; }
    remove() { this.removed=true; }
    focus() {
      if (document.activeElement===this) return;
      const previous=document.activeElement;
      document.activeElement=this;
      if(previous) emit(previous,'blur',{relatedTarget:this});
      emit(this,'focus',{relatedTarget:previous});
    }
  }
  document.createElement=()=>new Element();
  globalThis.document=document;
  globalThis.window=new EventTarget();
  return {document,canvas:()=>Object.assign(new Element(),{width:100,height:100,
    getBoundingClientRect:()=>({left:0,top:0,width:100,height:100}),setPointerCapture(){},
    getContext:()=>({clearRect(){},resetTransform(){}})})};
}
export function emit(target,type,extra={}) {
  const event=new Event(type,{cancelable:true});
  for(const [key,value] of Object.entries(extra)) Object.defineProperty(event,key,{value});
  target.dispatchEvent(event);
  return event;
}
