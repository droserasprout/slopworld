// Drawing commands originate in the installed Unity GUI controls through IGuiBackend.
import { tintPixels } from './gui-tint.js';
import { decodeGuiQuads } from './gui-quads.js';
import { decodeGuiPixels } from './gui-pixels.js';
const renderTargets = new Map();
const images = new Map();

// This experiment owns only Canvas replay. Native cache invalidation, row layout,
// command order and target sampling remain unchanged. Snapshots contain exact
// command strings, never pixels or references to mutable packet arrays.
export class GuiTargetDamagePlanner {
  constructor(entryLimit = 8, byteLimit = 2 * 1024 * 1024) {
    this.entryLimit = entryLimit; this.byteLimit = byteLimit;
    this.entries = new Map(); this.retainedBytes = 0;
    this.rejections = {}; this.bypassed = 0; this.reusedRows = 0;
  }
  invalidate() { this.entries.clear(); this.retainedBytes = 0; }
  forget(id) {
    const old = this.entries.get(id);
    if (old) this.retainedBytes -= old.bytes;
    this.entries.delete(id);
  }
  prepare(id, width, height, matrix, clips, body, resources, generation) {
    const reject = reason => {
      this.rejections[reason] = Math.min(Number.MAX_SAFE_INTEGER,(this.rejections[reason]??0)+1);
      return null;
    };
    const old = this.entries.get(id);
    this.forget(id);
    // This cheap comparison only declines optimization; it never certifies pixels.
    // Scrolling changes every row, so avoid decoding and serializing each glyph
    // merely to discover that there is nothing to preserve. A declined frame
    // replaces the old certificate: later reuse cannot bridge unexamined writes.
    const raw = rawRowKeys(body);
    const bypass = raw && old?.raw && !raw.some((key,i) => key.key === old.raw[i]?.key);
    let next;
    if (bypass) {
      this.bypassed = Math.min(Number.MAX_SAFE_INTEGER,this.bypassed+1);
      next = {header:null,groups:[],bytes:0};
    } else next = certifyTarget(width, height, matrix, clips, body, resources, generation,reject,raw,old,()=>{this.reusedRows=Math.min(Number.MAX_SAFE_INTEGER,this.reusedRows+1);});
    if (next) { next.raw = raw; next.bytes += raw?.reduce((n,key) => n+key.key.length*2+32,0) ?? 0; }
    if (!next || next.bytes > this.byteLimit || this.entryLimit <= 0) return null;
    while (this.entries.size >= this.entryLimit || this.retainedBytes + next.bytes > this.byteLimit)
      this.forget(this.entries.keys().next().value);
    this.entries.set(id, next); this.retainedBytes += next.bytes;
    if (!next.header || !old || old.header !== next.header) return null;

    // A removed/reordered group damages its OLD footprint too. Comparing at the
    // same ordered position deliberately gives up reuse after an insertion.
    const changed = [];
    for (let i = 0; i < Math.max(old.groups.length, next.groups.length); i++) {
      if (old.groups[i]?.key === next.groups[i]?.key) continue;
      for (const group of [old.groups[i], next.groups[i]])
        if (group) changed.push([Math.floor(group.box[1]) - 2, Math.ceil(group.box[3]) + 2]);
    }
    // Every admitted operation after the opaque reset belongs to a bounded row
    // group. Outside changed OLD and NEW group footprints, the complete ordered
    // raster sequence is identical, including fractional row clip edges. Preserve
    // the reset interior there, rather than introducing a hole per row.
    if (!next.groups.some((group,i) => group.key === old.groups[i]?.key)) return null;
    const box = next.interior;
    const x0 = Math.ceil(box[0]) + 1, x1 = Math.floor(box[2]) - 1;
    let bands = [[Math.ceil(box[1]) + 1, Math.floor(box[3]) - 1]];
    for (const [top,bottom] of changed) bands = bands.flatMap(([a,b]) =>
      bottom <= a || top >= b ? [[a,b]] : [[a,Math.min(b,top)],[Math.max(a,bottom),b]]);
    const safe = x1 > x0 ? bands.filter(([a,b]) => b > a).map(([a,b]) => [x0,a,x1,b]) : [];
    // Subtracting intervals above preserves order and disjointness. Every band
    // shares the same x bounds, so no second edge sweep or span merge is needed.
    const preserved = safe;
    if (preserved.length > 4096) return null;
    return preserved.length ? {preserved, width, height} : null;
  }
}

function rawRowKeys(body) {
  if (body.length > 16384) return null;
  const rows=[]; let start=-1,depth=0;
  for(let i=0;i<body.length;i++) {
    if(body[i].kind==='clipScreen') { if(depth++===0)start=i; }
    else if(body[i].kind==='pop') {
      if(!depth)return null;
      if(--depth===0)rows.push({key:exactKey(body.slice(start,i+1)),start,end:i});
    }
  }
  return !depth && rows.length && rows.length<=2048 ? rows : null;
}

const finiteArray = (v,n) => (Array.isArray(v) || ArrayBuffer.isView(v)) && v.length === n && Array.from(v).every(Number.isFinite);
const exactKey = v => JSON.stringify(v,(_key,value) =>
  typeof value==='string' ? 's:'+value : Object.is(value,-0) ? 'n:-0' : value);
const axisMatrix = m => finiteArray(m,6) && m[0] > 0 && m[3] > 0 && m[1] === 0 && m[2] === 0;
const positiveRect = r => finiteArray(r,4) && r[2] > 0 && r[3] > 0;
const intersectBox = (a,b) => [Math.max(a[0],b[0]),Math.max(a[1],b[1]),Math.min(a[2],b[2]),Math.min(a[3],b[3])];
const rectBox = (r,m) => [r[0]*m[0]+m[4],r[1]*m[3]+m[5],(r[0]+r[2])*m[0]+m[4],(r[1]+r[3])*m[3]+m[5]];
const physicalBox = r => [r[0],r[1],r[0]+r[2],r[1]+r[3]];
function imageGeometry(kind, v, resources, reset = false) {
  const entry = resources.get(v.id);
  if (!entry || !Number.isSafeInteger(entry.generation) || !finiteArray(v.color,4) || v.color.some(c => c < 0 || c > 1)) return null;
  let quads;
  if (kind === 'imageBatch') quads = Array.from(decodeGuiQuads(v.quads));
  else if (kind === 'imageTexture' && finiteArray(v.rect,4) && finiteArray(v.uv,4)) quads = [...v.rect,...v.uv];
  else return null;
  if (!quads.length || quads.length % 8 || !quads.every(Number.isFinite)) return null;
  for (let i=0;i<quads.length;i+=8) {
    if (quads[i+2] < 0 || quads[i+3] < 0) return null;
    // Only the reset needs a proof of opaque sampling. Row UVs stay exact,
    // including native float-rounded atlas edges outside [0,1], signed source
    // crops and zero-area no-ops. The enclosing row clip bounds their damage.
    if (reset && (quads[i+2] === 0 || quads[i+3] === 0 || quads[i+6] <= 0 || quads[i+7] <= 0 ||
        quads[i+4] < 0 || quads[i+5] < 0 || quads[i+4]+quads[i+6] > 1 || quads[i+5]+quads[i+7] > 1)) return null;
  }
  return {quads, key:[kind,v.id,entry.generation,v.color,quads], entry};
}
function certifyTarget(width, height, entryMatrix, clips, body, resources, generation,reject,raw,old,reused) {
  if (!Number.isSafeInteger(width) || !Number.isSafeInteger(height) || width <= 0 || height <= 0 || !axisMatrix(entryMatrix) || body.length > 16384) return reject('entry');
  let matrix = [...entryMatrix], interior = [0,0,width,height];
  for (const points of clips) {
    if (points.length !== 4 || !points.every(p => Number.isFinite(p.x) && Number.isFinite(p.y)) ||
        points[0].y !== points[1].y || points[1].x !== points[2].x || points[2].y !== points[3].y || points[3].x !== points[0].x ||
        points[2].x <= points[0].x || points[2].y <= points[0].y) return reject('entryClip');
    interior = intersectBox(interior,[points[0].x,points[0].y,points[2].x,points[2].y]);
  }
  const prefix = [], groups = [], stack = [];
  let reset = null, group = null;
  for (let commandIndex=0;commandIndex<body.length;commandIndex++) {
    const {kind,value:v}=body[commandIndex];
    if (kind === 'matrix') {
      if (!axisMatrix(v)) return reject('matrix');
      matrix = [...v];
      if (group) group.commands.push([kind,v]);
      else if (!reset) prefix.push([kind,v]);
      continue;
    }
    if (!reset) {
      let rect, key;
      if (kind === 'fill' && v.rounding === 0 && positiveRect(v.rect) && finiteArray(v.color,4) && v.color.every(c => c >= 0 && c <= 1) && v.color[3] === 1) {
        rect = v.rect; key = [kind,v.rect,v.color];
      } else {
        const image = imageGeometry(kind,v,resources,true);
        if (!image) return reject('resetShapeOrResource');
        if (image.quads.length !== 8) return reject('resetBatch');
        if (!image.entry.opaqueUniform || v.color[3] !== 1) return reject('resetOpacity');
        rect = image.quads.slice(0,4); key = image.key;
      }
      reset = [prefix,matrix,key];
      interior = intersectBox(interior,rectBox(rect,matrix));
      continue;
    }
    if (kind === 'clipScreen') {
      if (!positiveRect(v)) return reject('rowClip');
      if (!group) {
        const previous=old?.groups[groups.length], source=raw?.[groups.length];
        // Only the immediately preceding certified row can be reused. Exact raw
        // commands, entry transform and each image generation must still match.
        // A balanced row restores its entry matrix, so skipping its validation
        // leaves the outer state exactly as a full walk would.
        if(previous && source?.start===commandIndex && source.key===old.raw?.[groups.length]?.key &&
            previous.matrixKey===exactKey(matrix) && previous.resources.every(([id,version])=>resources.get(id)?.generation===version)) {
          reused();groups.push(previous);commandIndex=source.end;continue;
        }
        group = {box:physicalBox(v), commands:[['entry',matrix]],matrixKey:exactKey(matrix),resources:new Map()};
      }
      group.commands.push([kind,v]); stack.push(matrix); continue;
    }
    if (kind === 'pop') {
      if (!stack.length) return reject('unbalancedPop');
      group.commands.push([kind,v]); matrix = stack.pop();
      if (!stack.length) { groups.push({box:group.box,key:exactKey(group.commands),matrixKey:group.matrixKey,resources:[...group.resources]}); group = null; }
      continue;
    }
    if (!group) return reject('ungroupedDraw');
    const image = imageGeometry(kind,v,resources);
    if (!image) return reject('rowShapeOrResource');
    group.commands.push(image.key);
    group.resources.set(v.id,image.entry.generation);
  }
  if (!reset || stack.length || !groups.length || groups.length > 2048 || interior.some(v => !Number.isFinite(v))) return reject('incomplete');
  const header = exactKey([width,height,generation,entryMatrix,clips,reset]);
  const bytes = 2 * (header.length + groups.reduce((n,g) => n+g.key.length+g.matrixKey.length+g.resources.length*32+64,0));
  return {header,groups,interior,bytes};
}

let targetDamageEnabled = false, imageGeneration = 0;
const targetDamage = new GuiTargetDamagePlanner();
let damageScratch = null;
let targetDamageCounters = {writes:0,planned:0,preservedPixels:0,skippedDraws:0};
export function configureGuiTargetDamage(enabled) {
  const previous = targetDamageEnabled; targetDamageEnabled = !!enabled;
  targetDamage.invalidate(); renderRevision++;
  targetDamage.rejections = {};
  targetDamage.bypassed = 0;
  targetDamage.reusedRows = 0;
  if (!targetDamageEnabled) damageScratch = null;
  targetDamageCounters = {writes:0,planned:0,preservedPixels:0,skippedDraws:0};
  return previous;
}
export const guiTargetDamageStats = () => ({...targetDamageCounters, snapshots:targetDamage.entries.size, retainedBytes:targetDamage.retainedBytes,rejections:{...targetDamage.rejections},bypassed:targetDamage.bypassed,reusedRows:targetDamage.reusedRows,scratchBytes:damageScratch ? damageScratch.width*damageScratch.height*4 : 0});
const countDamage = (key,n=1) => { targetDamageCounters[key] = Math.min(Number.MAX_SAFE_INTEGER,targetDamageCounters[key]+n); };
let damageScreen = null;
const damageCanvases = new WeakSet();
const targetGenerations = new WeakMap();
let targetGeneration = 0;
function watchDamageCanvas(canvas) {
  if (damageCanvases.has(canvas)) return;
  damageCanvases.add(canvas);
  for (const event of ['contextlost','contextrestored']) canvas.addEventListener?.(event,() => { targetDamage.invalidate(); renderRevision++; });
}
function copyDamageTarget(target) {
  damageScratch ??= document.createElement('canvas');
  watchDamageCanvas(damageScratch);
  if(damageScratch.width!==target.width || damageScratch.height!==target.height) {
    damageScratch.width=target.width;damageScratch.height=target.height;
  }
  const saved=damageScratch.getContext('2d');
  if(!saved || saved.isContextLost?.()) { damageScratch=null;return false; }
  saved.resetTransform();saved.globalCompositeOperation='copy';
  saved.globalAlpha=1;saved.imageSmoothingEnabled=false;saved.drawImage(target,0,0);
  if(saved.isContextLost?.()) { damageScratch=null;return false; }
  return true;
}
// A constant upload has the same normalized samples at every resolution.
// Keep drawImage's clipping, edge coverage and opacity path, but expose a 1x1
// backing so software Canvas can avoid filtering a stretched multi-pixel source.
// Weak keys follow immutable upload/tint lifetime; mutable targets never qualify.
const uniformSources=new WeakMap();
let uniformImagesEnabled=true;
export function configureGuiUniformImages(enabled) {
  const previous=uniformImagesEnabled;uniformImagesEnabled=!!enabled;targetDamage.invalidate();renderRevision++;return previous;
}
function rememberUniformSource(source,pixels) {
  if(pixels.length>16*16*4 || pixels.length<=4)return;
  for(let i=4;i<pixels.length;i++)if(pixels[i]!==pixels[i%4])return;
  const single=document.createElement('canvas');single.width=1;single.height=1;
  single.getContext('2d').putImageData(new ImageData(new Uint8ClampedArray(pixels.slice(0,4)),1,1),0,0);
  uniformSources.set(source,single);
}
let renderRevision=0;
export const guiRenderRevision=()=>renderRevision;
export const guiTargetCount = () => renderTargets.size;
export const guiImageCount = () => images.size;

// Opt-in counters distinguish atlas churn from mutable-target tinting without
// retaining pixels, colors, or command contents. Enabling starts a fresh sample.
let tintProfile = null;
export function configureGuiTintProfile(enabled) {
  const previous = tintProfile !== null;
  tintProfile = enabled ? {imageMisses:0,targetMisses:0,imagePixels:0,targetPixels:0} : null;
  return previous;
}
export const guiTintProfile = () => tintProfile ? {...tintProfile} : null;

// Keep up to 32 colors for small atlases; the existing per-image byte budget
// still limits larger images. Sixteen entries churn on normal agent UI colors.
let expandedTintCache = true;
export function configureGuiTintCache(enabled) {
  const previous = expandedTintCache;
  expandedTintCache = !!enabled;
  // Comparison phases start cold, including repeated enabled phases.
  for(const entry of images.values())entry.tints.clear();
  targetDamage.invalidate(); renderRevision++;
  return previous;
}

// Only clipScreen resets to physical coordinates before recording its rectangle.
// Keep the generic transformed path available for diagnostics and other clips.
let screenClipFastPath = true;
export function configureGuiScreenClipFastPath(enabled) {
  const previous = screenClipFastPath;
  screenClipFastPath = !!enabled;
  targetDamage.invalidate(); renderRevision++;
  return previous;
}

function tintImage(source, color, cachedPixels = null) {
  const tinted = document.createElement('canvas'); tinted.width = source.width; tinted.height = source.height;
  const pixels = cachedPixels ?? source.getContext('2d').getImageData(0, 0, source.width, source.height).data;
  const data=tintPixels(pixels,color);
  tinted.getContext('2d').putImageData(new ImageData(data, source.width, source.height), 0, 0);
  rememberUniformSource(tinted,data);
  return tinted;
}
export function renderGui(canvas, commands) {
  if (targetDamageEnabled) {
    const dimensions = [canvas,canvas.width,canvas.height];
    if (!damageScreen || dimensions.some((v,i) => v !== damageScreen[i])) targetDamage.invalidate();
    damageScreen = dimensions;
    watchDamageCanvas(canvas);
  }
  try { renderGuiCommands(canvas,commands); }
  catch (error) { targetDamage.invalidate(); throw error; }
}
function renderGuiCommands(canvas, commands) {
  renderRevision++;
  const screen = canvas.getContext('2d');
  let ctx = screen, activeTarget = 0;
  let damage = null;
  const restorePreserved = () => {
    if (!damage) return;
    // Extra clipping changes accelerated Canvas edge rasterization. Keep the
    // original clip and draw order instead, then restore only proven opaque
    // integer interiors. Opaque source-over is an exact pixel replacement;
    // using the copy operator here could clear pixels outside each rectangle.
    ctx.save();ctx.resetTransform();ctx.globalAlpha=1;
    ctx.globalCompositeOperation='source-over';ctx.imageSmoothingEnabled=false;
    for(const [left,top,right,bottom] of damage.preserved)
      ctx.drawImage(damageScratch,left,top,right-left,bottom-top,left,top,right-left,bottom-top);
    ctx.restore();
  };
  const clips = [], clipDepths = [];
  ctx.resetTransform();
  const color = c => `rgba(${c[0] * 255},${c[1] * 255},${c[2] * 255},${c[3]})`;
  const clip = (rect, physical = false) => {
    const [x, y, w, h] = rect;
    let points;
    if (physical) {
      // Preserve corner order, signed extents and fractional coordinates for
      // target inheritance. The Canvas path below still owns raster clipping.
      points = [{x,y},{x:x+w,y},{x:x+w,y:y+h},{x,y:y+h}];
    } else {
      const matrix = ctx.getTransform();
      points = [[x,y],[x+w,y],[x+w,y+h],[x,y+h]].map(([x,y]) => new DOMPoint(x,y).matrixTransform(matrix));
    }
    clips.push(points);
    ctx.beginPath(); ctx.rect(...rect); ctx.clip();
  };
  const sample = (source, v) => {
    const [u, t, w, h] = v.uv;
    ctx.drawImage(source, u * source.width, (1 - t - h) * source.height, w * source.width, h * source.height, ...v.rect);
  };
  const preservedDraw = (x,y,w,h,m) => {
    if (!damage) return false;
    // Include a physical pixel for sampling/edge coverage. A draw wholly inside
    // one preserved rectangle cannot contribute to the damage complement.
    const left=x*m.a+m.e-1, top=y*m.d+m.f-1, right=(x+w)*m.a+m.e+1, bottom=(y+h)*m.d+m.f+1;
    // Bands are sorted vertically. Find the last possible band, then inspect
    // only its disjoint horizontal spans rather than scanning every row.
    const boxes=damage.preserved;
    let lo=0,hi=boxes.length;
    while(lo<hi) { const mid=(lo+hi)>>>1; if(boxes[mid][1]<=top)lo=mid+1;else hi=mid; }
    let skip=false;
    if (lo) {
      const bandTop=boxes[lo-1][1];
      for(let i=lo-1;i>=0 && boxes[i][1]===bandTop;i--) {
        const b=boxes[i];
        if(left>=b[0] && right<=b[2] && bottom<=b[3]) { skip=true;break; }
      }
    }
    if (skip) countDamage('skippedDraws');
    return skip;
  };
  for (let commandIndex=0;commandIndex<commands.length;commandIndex++) {
    const {kind,value:v} = commands[commandIndex];
    switch (kind) {
      case 'fill':
        ctx.fillStyle = color(v.color);
        ctx.beginPath(); ctx.roundRect(...v.rect, v.rounding); ctx.fill(); break;
      case 'text': {
        ctx.save();
        const [x, y, w, h] = v.rect;
        ctx.beginPath(); ctx.rect(x, y, w, h); ctx.clip();
        ctx.fillStyle = color(v.color);
        ctx.font = `${v.style & 2 ? 'italic ' : ''}${v.style & 1 ? 'bold ' : ''}${v.fontSize || 16}px monospace`;
        ctx.textBaseline = 'top';
        const horizontal = v.anchor % 3, vertical = Math.floor(v.anchor / 3);
        ctx.textAlign = ['left', 'center', 'right'][horizontal];
        ctx.fillText(v.text, x + w * horizontal / 2, y + Math.max(0, h - (v.fontSize || 16)) * vertical / 2);
        ctx.restore(); break;
      }
      case 'image': {
        targetDamage.invalidate();
        const source = document.createElement('canvas'); source.width = v.width; source.height = v.height;
        const data = decodeGuiPixels(v.pixels);
        source.getContext('2d').putImageData(new ImageData(data, v.width, v.height), 0, 0);
        rememberUniformSource(source,data);
        const opaqueUniform = data.length <= 16*16*4 && data[3] === 255 && data.every((channel,i) => channel === data[i%4]);
        images.set(v.id, { source, tints: new Map(), pixels: null, generation:++imageGeneration, opaqueUniform }); break;
      }
      case 'imageTexture':
      case 'imageBatch': {
        const entry = images.get(v.id);
        if (!entry) throw new Error(`Missing GUI image ${v.id}`);
        let source = entry.source;
        if (v.color.slice(0,3).some(channel => channel !== 1)) {
          const key = v.color.slice(0,3).join(',');
          if (!entry.tints.has(key)) {
            const limit = Math.max(1, Math.min(expandedTintCache ? 32 : 16, Math.floor(32 * 1024 * 1024 / (source.width * source.height * 4))));
            if (entry.tints.size >= limit) entry.tints.delete(entry.tints.keys().next().value);
            // Read back once per immutable upload. Using the Canvas round trip
            // retains its premultiplied-alpha quantization from the old path.
            entry.pixels ??= source.getContext('2d').getImageData(0, 0, source.width, source.height).data;
            if(tintProfile){tintProfile.imageMisses++;tintProfile.imagePixels+=source.width*source.height;}
            entry.tints.set(key, tintImage(source, v.color, entry.pixels));
          }
          source = entry.tints.get(key);
          entry.tints.delete(key); entry.tints.set(key, source);
        }
        if(uniformImagesEnabled)source=uniformSources.get(source)??source;
        ctx.save(); ctx.globalAlpha = v.color[3];
        if (kind === 'imageBatch') {
          const q=decodeGuiQuads(v.quads), width=source.width, height=source.height;
          const matrix=damage ? ctx.getTransform() : null;
          // Retain decoded geometry for profiling and any repeat playback.
          v.quads=q;
          for (let i=0; i<q.length; i+=8) {
            if (damage && preservedDraw(q[i],q[i+1],q[i+2],q[i+3],matrix)) continue;
            ctx.drawImage(source, q[i+4]*width, (1-q[i+5]-q[i+7])*height,
              q[i+6]*width, q[i+7]*height, q[i], q[i+1], q[i+2], q[i+3]);
          }
        } else if (!damage || !preservedDraw(...v.rect,ctx.getTransform())) sample(source, v);
        ctx.restore(); break;
      }
      case 'releaseImage': targetDamage.invalidate(); images.delete(v); break;
      case 'target': {
        const transform = ctx.getTransform();
        if (activeTarget) { restorePreserved();ctx.restore(); }
        damage = null;
        activeTarget = v.id;
        if (!v.id) { ctx = screen; ctx.setTransform(transform); break; }
        let target = renderTargets.get(v.id);
        if (!target) { target = document.createElement('canvas'); renderTargets.set(v.id, target); }
        if (!targetGenerations.has(target)) targetGenerations.set(target,++targetGeneration);
        if (targetDamageEnabled) watchDamageCanvas(target);
        if (target.width !== v.width || target.height !== v.height) { targetDamage.forget(v.id); target.width = v.width; target.height = v.height; }
        // Retained drawing targets keep the same readback preference in probes.
        ctx = target.getContext('2d', {willReadFrequently:false,colorType: screen.getContextAttributes?.().colorType ?? 'unorm8'}); ctx.save(); ctx.resetTransform();
        // Native GUI targets inherit the current physical clip and transform.
        for (const points of clips) {
          ctx.beginPath(); ctx.moveTo(points[0].x, points[0].y);
          for (const point of points.slice(1)) ctx.lineTo(point.x, point.y);
          ctx.closePath(); ctx.clip();
        }
        if (targetDamageEnabled) {
          countDamage('writes');
          let end = commandIndex+1;
          while (end < commands.length && commands[end].kind !== 'target') end++;
          // Never cache an unterminated write, even before error handling runs.
          const storageFits = target.width*target.height*4<=64*1024*1024 &&
            (ctx.getContextAttributes?.().colorType??'unorm8')==='unorm8' && !ctx.isContextLost?.();
          if (end < commands.length && storageFits) damage = targetDamage.prepare(v.id,target.width,target.height,
            [transform.a,transform.b,transform.c,transform.d,transform.e,transform.f],clips,
            commands.slice(commandIndex+1,end),images,targetGenerations.get(target));
          else targetDamage.forget(v.id);
          if(damage && !copyDamageTarget(target)) { targetDamage.forget(v.id);damage=null; }
          if (damage) {
            countDamage('planned');
            for (const [left,top,right,bottom] of damage.preserved) {
              countDamage('preservedPixels',(right-left)*(bottom-top));
            }
          }
        }
        ctx.setTransform(transform); break;
      }
      case 'targetTexture': {
        let source = renderTargets.get(v.id);
        if (!source) throw new Error(`Missing GUI render target ${v.id}`);
        if (v.color.slice(0,3).some(channel => channel !== 1)) {
          if(tintProfile){tintProfile.targetMisses++;tintProfile.targetPixels+=source.width*source.height;}
          source = tintImage(source, v.color);
        }
        ctx.save(); ctx.globalAlpha = v.color[3]; ctx.imageSmoothingEnabled = false;
        sample(source, v); ctx.restore(); break;
      }
      case 'releaseTarget': targetDamage.forget(v); renderTargets.delete(v); break;
      case 'matrix': ctx.setTransform(...v); break;
      case 'group': ctx.save(); clipDepths.push(clips.length); clip(v.rect); ctx.translate(v.rect[0] - v.offset[0], v.rect[1] - v.offset[1]); break;
      case 'clipScreen': { const transform = ctx.getTransform(); ctx.save(); clipDepths.push(clips.length); ctx.resetTransform(); clip(v, screenClipFastPath); ctx.setTransform(transform); break; }
      case 'clip': ctx.save(); clipDepths.push(clips.length); clip(v); break;
      case 'pop': ctx.restore(); clips.length = clipDepths.pop(); break;
      default: throw new Error(`Unknown GUI command: ${kind}`);
    }
  }
  if (activeTarget || clipDepths.length) throw new Error('Unclosed GUI target or clip');
  ctx.resetTransform();
}
export function renderGuiProbe(commands, controls = true, existing = null) {
  const canvas = existing ?? document.createElement('canvas');
  if (!existing) { canvas.width = controls ? 640 : 1280; canvas.height = controls ? 200 : 720; document.body.prepend(canvas); }
  if (!existing) renderGui(canvas, commands);
  const pixels = canvas.getContext('2d').getImageData(0, 0, canvas.width, canvas.height).data;
  const alpha = (x, y) => pixels[(y * canvas.width + x) * 4 + 3];
  if (controls && (alpha(405, 25) !== 255 || alpha(398, 25) !== 0 || alpha(422, 25) !== 0)) throw new Error('GUI group drawing escaped its clip');
  if (controls) {
    const rgb = (x, y) => Array.from(pixels.slice((y * canvas.width + x) * 4, (y * canvas.width + x) * 4 + 3)).join(',');
    if (rgb(455, 22) !== '0,0,255' || rgb(455, 37) !== '255,0,0') throw new Error('Unity texture rows were not converted to Canvas orientation');
  }
  const visible = pixels.filter((value, i) => i % 4 === 3 && value !== 0).length;
  if (!visible) throw new Error('Installed GUI produced no visible pixels');
  // The controls diagnostic exercises the fallback text command. A workspace
  // uses native atlas image batches and retained terminal targets instead;
  // command spelling cannot establish whether its text rendered correctly.
  if (controls && !commands.some(c => c.kind === 'text'))
    throw new Error('Installed controls produced no text commands');
  return { commands: commands.length, visiblePixels: visible, source: controls ? 'UnityEngine.GUI.Label/Box → IGuiBackend → Canvas2D' : 'SlopWorld.WorkspaceWindow → IGuiBackend → Canvas2D' };
}
