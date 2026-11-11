// Drawing commands originate in the installed Unity GUI controls through IGuiBackend.
import { tintPixels } from './gui-tint.js';
import { decodeGuiQuads } from './gui-quads.js';
const renderTargets = new Map();
const images = new Map();
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
  return previous;
}

// Only clipScreen resets to physical coordinates before recording its rectangle.
// Keep the generic transformed path available for diagnostics and other clips.
let screenClipFastPath = false;
export function configureGuiScreenClipFastPath(enabled) {
  const previous = screenClipFastPath;
  screenClipFastPath = !!enabled;
  return previous;
}

function tintImage(source, color, cachedPixels = null) {
  const tinted = document.createElement('canvas'); tinted.width = source.width; tinted.height = source.height;
  const pixels = cachedPixels ?? source.getContext('2d').getImageData(0, 0, source.width, source.height).data;
  tinted.getContext('2d').putImageData(new ImageData(tintPixels(pixels, color), source.width, source.height), 0, 0);
  return tinted;
}
export function renderGui(canvas, commands) {
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
    ctx.drawImage(source, u * source.width, (1 - t - h) * source.height, w * source.width, h * source.height, ...v.rect);
  };
  for (const { kind, value: v } of commands) {
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
        const data = Uint8ClampedArray.from(atob(v.pixels), c => c.charCodeAt(0));
        source.getContext('2d').putImageData(new ImageData(data, v.width, v.height), 0, 0);
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
        ctx.save(); ctx.globalAlpha = v.color[3];
        if (kind === 'imageBatch') {
          const q=decodeGuiQuads(v.quads), width=source.width, height=source.height;
          // Retain decoded geometry for profiling and any repeat playback.
          v.quads=q;
          for (let i=0; i<q.length; i+=8)
            ctx.drawImage(source, q[i+4]*width, (1-q[i+5]-q[i+7])*height,
              q[i+6]*width, q[i+7]*height, q[i], q[i+1], q[i+2], q[i+3]);
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
        if (!target) { target = document.createElement('canvas'); renderTargets.set(v.id, target); }
        if (target.width !== v.width || target.height !== v.height) { target.width = v.width; target.height = v.height; }
        ctx = target.getContext('2d', {colorType: screen.getContextAttributes?.().colorType ?? 'unorm8'}); ctx.save(); ctx.resetTransform();
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
