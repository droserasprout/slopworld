import { renderGui, guiTargetCount, guiImageCount } from './gui.js';

export function verifyGuiTargets(managed) {
  const canvas = document.createElement('canvas'); canvas.width = 40; canvas.height = 40;
  const ctx = canvas.getContext('2d'), before = guiTargetCount();
  const imagesBefore = guiImageCount();
  const pixel = (x,y) => Array.from(ctx.getImageData(x,y,1,1).data).join(',');
  for (let stage = 0; stage < 4; stage++) {
    const commands = JSON.parse(managed.GuiTargetFrame(stage));
    ctx.clearRect(0,0,40,40); renderGui(canvas, commands);
    if (stage < 2 && (pixel(5,5) !== '0,0,255,255' || pixel(25,5) !== '255,0,0,255' || pixel(5,25) !== '255,0,0,255'))
      throw new Error('Cached target lost its clip, pixels, or UV orientation');
    if (stage === 1 && commands.some(c => c.kind === 'target')) throw new Error('Cache hit unexpectedly repainted the target');
    if (stage === 1 && commands.some(c => c.kind === 'image')) throw new Error('Unchanged image was uploaded twice');
    if (stage < 3 && pixel(38,5) !== (stage === 2 ? '0,255,0,255' : '255,0,0,255')) throw new Error('Image Apply did not invalidate uploaded pixels');
    if (stage === 2 && pixel(5,5) !== '0,255,0,255') throw new Error('Recreated target retained stale pixels');
  }
  if (guiTargetCount() !== before) throw new Error('Released GUI target was retained');
  if (guiImageCount() !== imagesBefore) throw new Error('Disposed GUI image was retained');
  return { retained: true, recreated: true, released: true };
}
