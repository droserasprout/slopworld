// Pre-runtime presentation uses the mod's authored atlas. Game execution still
// starts in main.js; this module owns only the screen before its first frame.
import {workspaceViewport} from './workspace-viewport.js';
import {loadBackground} from './loading-background.js';
import {tintPixels} from './gui-tint.js';
import {loadingQuotes} from './loading-quotes.js';

export async function startLoading(parameters) {
  let viewport=workspaceViewport(parameters);
  document.title='SlopWorld';
  document.body.classList.add('workspace-app', 'loading');
  document.body.style.setProperty('--workspace-aspect', viewport.width/viewport.height);
  const canvas=document.createElement('canvas');
  canvas.id='loading-screen';
  canvas.width=viewport.width; canvas.height=viewport.height;
  canvas.setAttribute('aria-label', 'Loading SlopWorld');
  canvas.setAttribute('role', 'img');
  document.body.prepend(canvas);
  const context=canvas.getContext('2d');
  const atlas=new Image();
  atlas.src='./mod/LoadingFont.png';
  const cancellation=new AbortController();
  let stopped=false, background=null, backgroundAt=0, animation=0, glyphs=null, quotes=null;
  let previousGlyphs=null, latestGlyphs=null;
  let fontSize=24, message='Loading SlopWorld...';
  // Patch_LoadingTips: 32x64 cells, one-pixel gutters, 16 columns,
  // 0.8 glyph scale, 16 logical pixels of panel padding.
  const paint=() => {
    if(stopped) return;
    if(parameters.has('responsive')) {
      const next=workspaceViewport(parameters);
      if(next.width!==viewport.width || next.height!==viewport.height || next.uiScale!==viewport.uiScale) {
        viewport=next;canvas.width=next.width;canvas.height=next.height;
        document.body.style.setProperty('--workspace-aspect',next.width/next.height);
      }
    }
    context.setTransform(1,0,0,1,0,0);
    context.fillStyle='#000'; context.fillRect(0,0,canvas.width,canvas.height);
    const scale=viewport.uiScale, width=canvas.width/scale;
    context.setTransform(scale,0,0,scale,0,0);
    background?.draw(context,width,canvas.height/scale);
    if (!glyphs) return;
    const panel=Math.min(540, Math.max(280,width*.45), Math.max(1,width-40));
    const glyphWidth=fontSize*.8/2, glyphHeight=fontSize*.8;
    context.setTransform(scale,0,0,scale,0,0);
    let x=width-panel+16, y=16-(quotes?.halfStep ? .5 : 0)*fontSize*.8*60/64;
    const left=x, right=width-16;
    context.save();context.beginPath();context.rect(left,16,panel-32,canvas.height/scale-32);context.clip();
    const text=quotes?quotes.lines.join('\n'):message;
    if(quotes)canvas.dataset.quoteScrolls=String(quotes.scrolls);
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
  atlas.onload=()=>{
    if(stopped) return;
    glyphs=document.createElement('canvas');glyphs.width=atlas.width;glyphs.height=atlas.height;
    const target=glyphs.getContext('2d');target.drawImage(atlas,0,0);
    const pixels=target.getImageData(0,0,atlas.width,atlas.height);
    target.putImageData(new ImageData(tintPixels(pixels.data,[.82,.88,.93,1]),atlas.width,atlas.height),0,0);
    const variant=amount=>{
      const surface=document.createElement('canvas');surface.width=atlas.width;surface.height=atlas.height;
      surface.getContext('2d').putImageData(new ImageData(tintPixels(pixels.data,[.82*amount,.88*amount,.93*amount,1]),atlas.width,atlas.height),0,0);
      return surface;
    };
    previousGlyphs=variant(.75);latestGlyphs=variant(.55);
    canvas.dataset.atlas='ready';
    paint();
  };
  loadBackground(cancellation.signal).then(value=>{
    if(stopped) {value?.dispose();return;}
    background=value;backgroundAt=performance.now();
    if(!background) return;
    canvas.dataset.background='native-cache';
    paint();
  }).catch(error=>{if(error.name!=='AbortError') canvas.dataset.background='unavailable';});
  const update=()=>{
    if(stopped)return;
    const now=performance.now()/1000;
    const backgroundChanged=background?.advance(now-backgroundAt/1000);
    const quotesChanged=quotes?.advance(now);
    if(backgroundChanged || quotesChanged)paint();
    animation=requestAnimationFrame(update);
  };
  update();
  paint();
  // Presentation preferences are optional; the native default is 14 + 10.
  const preferences=parameters.has('referenceAppearance')
    ? fetch('./assets/reference-appearance.json',{signal:cancellation.signal}).then(response=>response.ok?response.json():null)
      .then(settings=>{
        const size=settings?.fontSize;
        if (Number.isFinite(size)) {fontSize=Math.max(8,Math.min(32,size+10));paint();}
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
    canvas.dataset.quotes='ready';quotes.advance(performance.now()/1000);paint();
  }).catch(error=>{if(error.name!=='AbortError')canvas.dataset.quotes='unavailable';});
  const finish=()=>{
    stopped=true;cancellation.abort();cancelAnimationFrame(animation);
    background?.dispose();canvas.remove();document.body.classList.remove('loading');
  };
  // Allow this screen to reach the compositor before loading the runtime module.
  // Background/throttled tabs may receive no animation callbacks. They must
  // still load, so a bounded timer also releases the runtime import.
  await new Promise(resolve=>{
    const timer=setTimeout(resolve,100);
    requestAnimationFrame(()=>requestAnimationFrame(()=>{clearTimeout(timer);resolve();}));
  });
  return {
    takeBackground() {
      const retained=background;
      background=null;
      return retained ? {animation:retained,started:backgroundAt/1000} : null;
    },
    status(text) {message=text;canvas.setAttribute('aria-label',text);paint();},
    finish,
  };
}
