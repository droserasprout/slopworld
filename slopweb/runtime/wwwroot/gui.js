// Drawing commands originate in the installed Unity GUI controls through IGuiBackend.
import { tintPixels } from './gui-tint.js';
import { decodeGuiQuads } from './gui-quads.js';
import { decodeGuiPixels } from './gui-pixels.js';
const renderTargets = new Map();
const images = new Map();

const watchedCanvases = new WeakSet();
let targetsNeedRepaint = false;
function resourceRestored() {
  // Retained targets may have sampled this surface while its pixels were lost.
  targetsNeedRepaint = true;
  renderRevision++;
}
export function takeGuiTargetInvalidation() {
  const pending = targetsNeedRepaint;
  targetsNeedRepaint = false;
  return pending;
}
function watchRenderCanvas(canvas) {
  if (watchedCanvases.has(canvas)) return;
  watchedCanvases.add(canvas);
  canvas.addEventListener?.('contextlost',() => { renderRevision++; });
  canvas.addEventListener?.('contextrestored',resourceRestored);
}
// A constant upload has the same normalized samples at every resolution.
// Keep drawImage's clipping, edge coverage and opacity path, but expose a 1x1
// backing so software Canvas can avoid filtering a stretched multi-pixel source.
// Weak keys follow immutable upload/tint lifetime; mutable targets never qualify.
const uniformSources=new WeakMap();
function paintImmutableUpload(canvas, pixels) {
  const image = new ImageData(pixels, canvas.width, canvas.height);
  const paint = () => canvas.getContext('2d').putImageData(image, 0, 0);
  paint();
  canvas.addEventListener?.('contextrestored', () => {
    paint();
    resourceRestored();
  });
}
function rememberUniformSource(source,pixels) {
  if(pixels.length>16*16*4 || pixels.length<=4)return;
  for(let i=4;i<pixels.length;i++)if(pixels[i]!==pixels[i%4])return;
  const single=document.createElement('canvas');single.width=1;single.height=1;
  paintImmutableUpload(single,pixels.slice(0,4));
  uniformSources.set(source,single);
}
let renderRevision=0;
export const guiRenderRevision=()=>renderRevision;

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
  renderRevision++;
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

function drawTexture(ctx, source, pattern, sx, sy, sw, sh, x, y, width, height) {
  if (!pattern) {
    ctx.drawImage(source, sx, sy, sw, sh, x, y, width, height);
    return;
  }
  if (!sw || !sh || !width || !height) return;
  const scaleX = width / sw, scaleY = height / sh;
  // Place the first requested source texel at the destination's local origin.
  pattern.setTransform({
    a: scaleX, d: scaleY,
    e: x - (sx % source.width) * scaleX, f: y - (sy % source.height) * scaleY,
  });
  ctx.fillStyle = pattern;
  ctx.fillRect(x, y, width, height);
}

export function renderGui(canvas, commands) {
  watchRenderCanvas(canvas);
  renderRevision++;
  const screen = canvas.getContext('2d');
  let ctx = screen, activeTarget = 0;
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
    const pattern = v.repeat ? ctx.createPattern(source, 'repeat') : null;
    drawTexture(ctx, source, pattern, u * source.width, (1 - t - h) * source.height,
      w * source.width, h * source.height, ...v.rect);
  };
  for (const {kind, value: v} of commands) {
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
        const source = document.createElement('canvas'); source.width = v.width; source.height = v.height;
        const data = decodeGuiPixels(v.pixels);
        paintImmutableUpload(source,data);
        rememberUniformSource(source,data);
        images.set(v.id, { source, tints: new Map(), pixels: null }); break;
      }
      case 'imageTexture':
      case 'imageBatch': {
        const entry = images.get(v.id);
        if (!entry) throw new Error(`Missing GUI image ${v.id}`);
        let source = entry.source;
        if (v.color.slice(0,3).some(channel => channel !== 1)) {
          const key = v.color.slice(0,3).join(',');
          if (!entry.tints.has(key)) {
            // A lost canvas can read back transparent pixels. Wait for
            // restoration before taking the first persistent tint source.
            if (!entry.pixels && source.getContext('2d').isContextLost?.()) break;
            const limit = Math.max(1, Math.min(expandedTintCache ? 32 : 16, Math.floor(32 * 1024 * 1024 / (source.width * source.height * 4))));
            if (entry.tints.size >= limit) entry.tints.delete(entry.tints.keys().next().value);
            // Read back once per immutable upload. Using the Canvas round trip
            // retains its premultiplied-alpha quantization from the old path.
            entry.pixels ??= source.getContext('2d').getImageData(0, 0, source.width, source.height).data;
            if(tintProfile){tintProfile.imageMisses++;tintProfile.imagePixels+=source.width*source.height;}
            const tinted = tintImage(source, v.color, entry.pixels);
            // Rebuild derived colors from the upload after their context is
            // restored; retaining a CPU bitmap per tint would double the cache.
            tinted.addEventListener?.('contextrestored', () => {
              if (entry.tints.get(key) === tinted) {
                entry.tints.delete(key);
                resourceRestored();
              }
            });
            entry.tints.set(key, tinted);
          }
          source = entry.tints.get(key);
          entry.tints.delete(key); entry.tints.set(key, source);
        }
        source=uniformSources.get(source)??source;
        ctx.save(); ctx.globalAlpha = v.color[3];
        // Building icons shrink far below their source resolution; request the
        // browser's downsampling filter instead of aliased single-pixel samples.
        ctx.imageSmoothingEnabled = true;
        ctx.imageSmoothingQuality = 'high';
        if (kind === 'imageBatch') {
          const pattern = v.repeat ? ctx.createPattern(source, 'repeat') : null;
          const q=decodeGuiQuads(v.quads), width=source.width, height=source.height;
          // Keep packet commands immutable for frame/row comparisons. The quad
          // cache already retains decoded geometry for profiling and replay.
          for (let i=0; i<q.length; i+=8) {
            drawTexture(ctx, source, pattern, q[i+4]*width, (1-q[i+5]-q[i+7])*height,
              q[i+6]*width, q[i+7]*height, q[i], q[i+1], q[i+2], q[i+3]);
          }
        } else sample(source, v);
        ctx.restore(); break;
      }
      case 'releaseImage': images.delete(v); break;
      case 'target': {
        const transform = ctx.getTransform();
        if (activeTarget) ctx.restore();
        activeTarget = v.id;
        if (!v.id) { ctx = screen; ctx.setTransform(transform); break; }
        let target = renderTargets.get(v.id);
        if (!target) {
          target = document.createElement('canvas');
          watchRenderCanvas(target);
          renderTargets.set(v.id, target);
        }
        if (target.width !== v.width || target.height !== v.height) { target.width = v.width; target.height = v.height; }
        // Retained drawing targets keep the same readback preference in probes.
        ctx = target.getContext('2d', {willReadFrequently:false,colorType: screen.getContextAttributes?.().colorType ?? 'unorm8'}); ctx.save(); ctx.resetTransform();
        // Native GUI targets inherit the current physical clip and transform.
        for (const points of clips) {
          ctx.beginPath(); ctx.moveTo(points[0].x, points[0].y);
          for (const point of points.slice(1)) ctx.lineTo(point.x, point.y);
          ctx.closePath(); ctx.clip();
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
      case 'releaseTarget': renderTargets.delete(v); break;
      case 'matrix': ctx.setTransform(...v); break;
      case 'group': ctx.save(); clipDepths.push(clips.length); clip(v.rect); ctx.translate(v.rect[0] - v.offset[0], v.rect[1] - v.offset[1]); break;
      case 'clipScreen': { const transform = ctx.getTransform(); ctx.save(); clipDepths.push(clips.length); ctx.resetTransform(); clip(v, true); ctx.setTransform(transform); break; }
      case 'clip': ctx.save(); clipDepths.push(clips.length); clip(v); break;
      case 'pop': ctx.restore(); clips.length = clipDepths.pop(); break;
      default: throw new Error(`Unknown GUI command: ${kind}`);
    }
  }
  if (activeTarget || clipDepths.length) throw new Error('Unclosed GUI target or clip');
  ctx.resetTransform();
}
