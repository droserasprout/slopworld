// Drawing commands originate in the installed Unity GUI controls through IGuiBackend.
const renderTargets = new Map();
const images = new Map();
export const guiTargetCount = () => renderTargets.size;
export const guiImageCount = () => images.size;

function tintImage(source, color) {
  const tinted = document.createElement('canvas'); tinted.width = source.width; tinted.height = source.height;
  const pixels = source.getContext('2d').getImageData(0, 0, source.width, source.height);
  for (let i = 0; i < pixels.data.length; i++) if (i % 4 !== 3) pixels.data[i] *= color[i % 4];
  tinted.getContext('2d').putImageData(pixels, 0, 0);
  return tinted;
}
export function renderGui(canvas, commands) {
  const screen = canvas.getContext('2d');
  let ctx = screen, activeTarget = 0;
  const clips = [], clipDepths = [];
  ctx.resetTransform();
  const color = c => `rgba(${c[0] * 255},${c[1] * 255},${c[2] * 255},${c[3]})`;
  const clip = rect => {
    const [x, y, w, h] = rect, matrix = ctx.getTransform();
    const points = [[x,y],[x+w,y],[x+w,y+h],[x,y+h]].map(([x,y]) => new DOMPoint(x,y).matrixTransform(matrix));
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
        images.set(v.id, { source, tints: new Map() }); break;
      }
      case 'imageTexture': {
        const entry = images.get(v.id);
        if (!entry) throw new Error(`Missing GUI image ${v.id}`);
        let source = entry.source;
        if (v.color.slice(0,3).some(channel => channel !== 1)) {
          const key = v.color.slice(0,3).join(',');
          if (!entry.tints.has(key)) {
            const limit = Math.max(1, Math.min(16, Math.floor(32 * 1024 * 1024 / (source.width * source.height * 4))));
            if (entry.tints.size >= limit) entry.tints.delete(entry.tints.keys().next().value);
            entry.tints.set(key, tintImage(source, v.color));
          }
          source = entry.tints.get(key);
        }
        ctx.save(); ctx.globalAlpha = v.color[3]; sample(source, v); ctx.restore(); break;
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
        ctx = target.getContext('2d'); ctx.save(); ctx.resetTransform();
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
        if (v.color.slice(0,3).some(channel => channel !== 1)) source = tintImage(source, v.color);
        ctx.save(); ctx.globalAlpha = v.color[3]; ctx.imageSmoothingEnabled = false;
        sample(source, v); ctx.restore(); break;
      }
      case 'releaseTarget': renderTargets.delete(v); break;
      case 'matrix': ctx.setTransform(...v); break;
      case 'group': ctx.save(); clipDepths.push(clips.length); clip(v.rect); ctx.translate(v.rect[0] - v.offset[0], v.rect[1] - v.offset[1]); break;
      case 'clipScreen': { const transform = ctx.getTransform(); ctx.save(); clipDepths.push(clips.length); ctx.resetTransform(); clip(v); ctx.setTransform(transform); break; }
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
  if (!visible || !commands.some(c => c.kind === 'text')) throw new Error('Installed controls produced no text pixels');
  return { commands: commands.length, visiblePixels: visible, source: controls ? 'UnityEngine.GUI.Label/Box → IGuiBackend → Canvas2D' : 'SlopWorld.WorkspaceWindow → IGuiBackend → Canvas2D' };
}
