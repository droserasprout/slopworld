import {loadBackground} from './loading-background.js';
import {tintPixels} from './gui-tint.js';
import {loadingQuotes} from './loading-quotes.js';

// Shared by the worker and the main-thread fallback. The page owns DOM sizing
// and accessibility. This renderer owns both animation clocks and their assets.
export function renderLoading(canvas, viewport, referenceAppearance, createSurface, loadAtlas, report=()=>{}) {
  canvas.width=viewport.width; canvas.height=viewport.height;
  const context=canvas.getContext('2d');
  const cancellation=new AbortController();
  const backgroundCancellation=new AbortController();
  let stopped=false, background=null, backgroundAt=null, animation=0, glyphs=null, quotes=null;
  // Each presentation starts when its own assets are ready. A slow or failed
  // optional asset must not stop the other loading animation.
  let original=null, backgroundTransferred=false;
  let previousGlyphs=null, latestGlyphs=null;
  let scrolls=-1;
  let fontSize=24, message='Loading SlopWorld...';
  // Patch_LoadingTips: 32x64 cells, one-pixel gutters, 16 columns,
  // 0.8 glyph scale, 16 logical pixels of panel padding.
  const paint=() => {
    if(stopped) return;
    context.setTransform(1,0,0,1,0,0);
    context.fillStyle='#000'; context.fillRect(0,0,canvas.width,canvas.height);
    const scale=viewport.uiScale, width=canvas.width/scale;
    context.setTransform(scale,0,0,scale,0,0);
    (background ?? original)?.draw(context,width,canvas.height/scale);
    if (!glyphs) return;
    const panel=Math.min(540, Math.max(280,width*.45), Math.max(1,width-40));
    const glyphWidth=fontSize*.8/2, glyphHeight=fontSize*.8;
    context.setTransform(scale,0,0,scale,0,0);
    let x=width-panel+16, y=16-(quotes?.halfStep ? .5 : 0)*fontSize*.8*60/64;
    const left=x, right=width-16;
    context.save();context.beginPath();context.rect(left,16,panel-32,canvas.height/scale-32);context.clip();
    if(quotes && scrolls!==quotes.scrolls) {scrolls=quotes.scrolls;report('quoteScrolls',String(scrolls));}
    const text=quotes?quotes.lines.join('\n'):message;
    const words=quotes?[...text.matchAll(/\S+/g)]:[];
    const latest=words.at(-1)?.index??Infinity, previous=words.at(-2)?.index??Infinity;
    for (let index=0;index<text.length;index++) {
      const char=text[index];
      if (char==='\n' || (!quotes && x+glyphWidth>right)) {x=left; y+=fontSize*.8*60/64;}
      if (char==='\n') continue;
      const code=char.charCodeAt(0), slot=(code>=32 && code<=126 ? code : 63)-32;
      context.drawImage(index>=latest?latestGlyphs:index>=previous?previousGlyphs:glyphs, (slot%16)*34+1, Math.floor(slot/16)*66+1,
        32,64,x,y,glyphWidth,glyphHeight);
      x+=glyphWidth;
    }
    context.restore();
  };
  loadAtlas(cancellation.signal).then(atlas=>{
    if(stopped) {atlas.close?.();return;}
    glyphs=createSurface(atlas.width,atlas.height);
    const target=glyphs.getContext('2d');target.drawImage(atlas,0,0);
    const pixels=target.getImageData(0,0,atlas.width,atlas.height);
    target.putImageData(new ImageData(tintPixels(pixels.data,[.82,.88,.93,1]),atlas.width,atlas.height),0,0);
    const variant=amount=>{
      const surface=createSurface(atlas.width,atlas.height);
      surface.getContext('2d').putImageData(new ImageData(tintPixels(pixels.data,[.82*amount,.88*amount,.93*amount,1]),atlas.width,atlas.height),0,0);
      return surface;
    };
    previousGlyphs=variant(.75);latestGlyphs=variant(.55);
    atlas.close?.();
    report('atlas','ready');paint();
  }).catch(()=>{});
  const startPlayback=value=>{
    if(stopped || backgroundTransferred || !value)return;
    background=value;backgroundAt=performance.now();
    report('background','native-cache');paint();
  };
  const backgroundReady=loadBackground(backgroundCancellation.signal,value=>{
    if(stopped) {value.dispose();return;}
    original=value;paint();
  },startPlayback).then(value=>{
    if(stopped && !backgroundTransferred){value?.dispose();return null;}
    if(!background)startPlayback(value);
    return value ? {animation:value,started:(backgroundAt??performance.now())/1000} : null;
  }).catch(()=>{
    background=null;
    if(!stopped){report('background','unavailable');paint();}
    return null;
  });
  const update=()=>{
    if(stopped)return;
    const now=performance.now()/1000;
    const backgroundChanged=background?.advance(now-backgroundAt/1000);
    const quotesChanged=glyphs && quotes?.advance(now);
    if(backgroundChanged || quotesChanged)paint();
    animation=requestAnimationFrame(update);
  };
  update();
  paint();
  // Presentation preferences are optional; the native default is 14 + 10.
  const preferences=referenceAppearance
    ? fetch('./assets/reference-appearance.json',{signal:cancellation.signal}).then(response=>response.ok?response.json():null)
      .then(settings=>{
        const size=settings?.fontSize;
        if (!stopped && Number.isFinite(size)) {fontSize=Math.max(8,Math.min(32,size+10));paint();}
        return settings;
      }).catch(()=>null) : Promise.resolve(null);
  Promise.all([preferences,fetch('./mod/loading-tips.txt',{signal:cancellation.signal}).then(response=>{
    if(!response.ok)throw new Error('Loading quotes unavailable');return response.text();
  })]).then(([settings,source])=>{
    if(stopped)return;
    const width=canvas.width/viewport.uiScale,height=canvas.height/viewport.uiScale;
    const panel=Math.min(540,Math.max(280,width*.45),Math.max(1,width-40));
    const columns=Math.max(1,Math.floor((panel-32)/(fontSize*.4)));
    const lines=Math.max(1,Math.floor((height-32-fontSize*.8)/(fontSize*.8*60/64))+1);
    quotes=loadingQuotes(source,columns,lines,viewport.uiScale,settings?.grandmaMode??false);
    report('quotes','ready');paint();
  }).catch(()=>{if(!stopped)report('quotes','unavailable');});
  const finish=()=>{
    stopped=true;cancellation.abort();
    if(!backgroundTransferred)backgroundCancellation.abort();
    cancelAnimationFrame(animation);
    // A pending load closes partial bitmaps on abort. Dispose completed sets
    // here only if this renderer still owns them.
    if(!backgroundTransferred)backgroundReady.then(value=>value?.animation.dispose());
    background=null;original?.dispose();
  };
  return {
    // Transfer in-flight downloads as well as completed frames. The workspace
    // owns cancellation and disposal after this one-time handoff.
    takeBackground() {
      backgroundTransferred=true;
      background=null;
      return {ready:backgroundReady,
        cancel:()=>backgroundCancellation.abort()};
    },
    resize(next) {
      viewport=next;canvas.width=next.width;canvas.height=next.height;paint();
    },
    status(text) {message=text;paint();},
    finish,
  };
}
