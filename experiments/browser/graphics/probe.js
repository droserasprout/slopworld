import { createSceneDefaultRenderer, identity, loadSceneShaders } from "./renderer.js";

window.__probeResult = { status: "running" };
const checks = [];
let renderer;
try {
  const sources = await loadSceneShaders();
  const manifest = await (await fetch("./manifest.json")).json();
  renderer = createSceneDefaultRenderer(document.querySelector("canvas"), sources);
  const vertexColor = [1, 0.5, 1, 1];
  const vertices = new Float32Array([[-1, -1], [3, -1], [-1, 3]].flatMap(([x, y]) => [x, y, 0.5, 0, 1, 0, ...vertexColor, (x + 1) / 2, (y + 1) / 2, 0, 0]));
  const base = { vertices, color: [0.5, 1, 0.5, 1], clear: [7 / 255, 11 / 255, 13 / 255, 1] };
  function check(name, expected) {
    const actual = renderer.readPixel(32, 32);
    const pass = actual.every((value, i) => Math.abs(value - expected[i]) <= 1);
    checks.push({ name, pass, expected, actual });
    if (!pass) throw new Error(`${name}: expected ${expected}, got ${actual}`);
  }
  renderer.draw(base);
  check("material times vertex color", [128, 128, 128, 255]);
  renderer.setTexture(new Uint8Array([64, 128, 192, 255]), 1, 1);
  renderer.draw({ ...base, textured: true });
  check("sampled texture times material times vertex color", [32, 64, 96, 255]);
  renderer.draw({ ...base, color: [1, 1, 1, 0] });
  check("alpha discard preserves clear", [7, 11, 13, 255]);
  const translated = identity();
  translated[12] = 10;
  renderer.draw({ ...base, model: translated });
  check("model push constant becomes uniform block", [7, 11, 13, 255]);
  renderer.draw({ ...base, viewProjection: translated });
  check("view projection descriptor becomes uniform block", [7, 11, 13, 255]);
  const invalidDepth = vertices.slice();
  for (let i = 2; i < invalidDepth.length; i += 14) invalidDepth[i] = -0.5;
  renderer.draw({ ...base, vertices: invalidDepth });
  check("Vulkan negative clip depth remains clipped", [7, 11, 13, 255]);
  const textureResponse = await fetch(manifest.texture.file);
  if (!textureResponse.ok) throw new Error(`Local texture: HTTP ${textureResponse.status}`);
  const bitmap = await createImageBitmap(await textureResponse.blob(), { premultiplyAlpha: "none", colorSpaceConversion: "none" });
  try {
    const reference = new OffscreenCanvas(bitmap.width, bitmap.height).getContext("2d");
    reference.drawImage(bitmap, 0, 0);
    const rgba = reference.getImageData(0, 0, bitmap.width, bitmap.height).data;
    let offset = 0;
    while (offset < rgba.length && rgba[offset + 3] !== 255) offset += 4;
    if (offset === rgba.length) throw new Error("Local texture has no opaque reference pixel");
    const x = (offset / 4) % bitmap.width;
    const y = Math.floor(offset / 4 / bitmap.width);
    const sampledVertices = vertices.slice();
    for (let i = 0; i < sampledVertices.length; i += 14) {
      sampledVertices[i + 10] = (x + 0.5) / bitmap.width;
      sampledVertices[i + 11] = (y + 0.5) / bitmap.height;
    }
    renderer.setTexture(bitmap);
    renderer.draw({ ...base, vertices: sampledVertices, textured: true });
    check("installed PNG decoded and sampled", [...rgba.slice(offset, offset + 3)].map(c => Math.round(c / 2)).concat(255));
  } finally { bitmap.close(); }
  renderer.draw({ ...base, textured: true });
  window.__probeResult = { status: "pass", backend: renderer.backend, checks, shaders: manifest.shaders, texture: manifest.texture, scope: manifest.scope };
} catch (error) {
  window.__probeResult = { status: "fail", checks, error: String(error), stack: error.stack };
} finally {
  renderer?.dispose();
  document.querySelector("#result").textContent = JSON.stringify(window.__probeResult, null, 2);
}
