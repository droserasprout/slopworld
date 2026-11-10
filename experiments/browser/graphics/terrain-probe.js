import { createTerrainRenderer } from './terrain-renderer.js';
import { identity } from './renderer.js';
let renderer;
try {
  renderer = await createTerrainRenderer(document.querySelector('canvas'), new URL('./', location.href), true);
  const vertices = [-1, -1, -1, 1, 1, 1, -1, -1, 1, 1, 1, -1]
    .reduce((all, _, i, xy) => i % 2 ? all : all.concat([xy[i], 0, xy[i+1], 1, 1, 1, 1]), []);
  const frame = {
    camera: { viewProjection: [1,0,0,0, 0,0,0,0, 0,-1,0,0, 0,0,0.5,1] },
    textures: [[255,0,0,255], [0,0,255,255], [255,255,255,255], [0,0,0,0]].map(rgba =>
      ({ width: 1, height: 1, rgba: btoa(String.fromCharCode(...rgba)) })),
    draws: [],
  };
  const draw = (shader, texture, alpha = 1) => ({ shader, texture, noise: 2, vertices, model: identity(), color: [1,1,1,alpha] });
  const checks = [];
  function check(name, expected) {
    renderer.draw(frame);
    const actual = renderer.readPixel(32, 32);
    const ok = actual.every((c, i) => Math.abs(c - expected[i]) <= 1);
    checks.push({ name, actual, expected, ok });
    if (!ok) throw new Error(`${name}: ${actual} expected ${expected}`);
  }
  frame.draws = [draw('TerrainHard', 0)]; check('terrain-hard', [255,0,0,255]);
  frame.draws.push(draw('TerrainFade', 1, 0.5)); check('terrain-fade-blending', [127,0,128,255]);
  frame.draws = [draw('TerrainFadeRough', 1)]; check('terrain-rough-opaque', [0,0,255,255]);
  const sprite = shader => ({ ...draw(shader, 1), stride: 11,
    vertices: vertices.flatMap((_, i) => i % 7 === 0 ? [...vertices.slice(i, i+7), 0.5, 0.5, 0, 0] : []),
    textureST: [0,0,1,1], colorTwo: [1,1,1,1], mask: 3 });
  frame.draws = [draw('TerrainHard', 0), sprite('Cutout')]; check('object-cutout', [0,0,255,255]);
  frame.draws[1].texture = 3; check('object-alpha-discard', [255,0,0,255]);
  frame.draws[1] = sprite('CutoutComplex'); check('object-black-mask-default', [0,0,255,255]);
  frame.draws[1] = sprite('Transparent'); frame.draws[1].color[3] = 0.5;
  check('object-transparent', [127,0,128,255]);
  // Use the same raw std140 payload path as the installed material resolver.
  const plant = sprite('CutoutPlant');
  const material = new Float32Array(128);
  material.set([1,1,1,1, 0,0,1,1]);
  plant.uniforms = btoa(String.fromCharCode(...new Uint8Array(material.buffer)));
  plant.fall = 2;
  frame.draws = [plant]; check('plant-resolved-uniforms', [0,0,255,255]);
  const skin = sprite('CutoutSkin');
  const skinData = new Float32Array(128);
  skinData.set([1,1,1,1, 0,0,1,1, 1,0,0,0]);
  skin.uniforms = btoa(String.fromCharCode(...new Uint8Array(skinData.buffer)));
  frame.textures.push({ width: 1, height: 1, rgba: btoa(String.fromCharCode(128,128,128,255)) });
  skin.texture = 4;
  frame.draws = [skin]; check('skin-shadow-color', [156,128,128,255]);
  const hair = sprite('CutoutHair');
  hair.color[3] = 0.25;
  frame.draws = [draw('TerrainHard', 0), hair]; check('hair-soft-alpha', [191,0,64,255]);
  hair.color[3] = 0.05; check('hair-alpha-discard', [255,0,0,255]);
  const edge = { ...draw('EdgeShadow', null), color: [0.5,0.5,0.5,1] };
  frame.draws = [draw('TerrainHard', 0), edge]; check('edge-shadow-multiply', [128,0,0,255]);
  const light = { ...draw('LightOverlay', null), color: [0.5,0.5,0.5,1],
    vertices: vertices.map((value, index) => index % 7 >= 3 ? 0 : value) };
  frame.draws = [draw('TerrainHard', 0), light]; check('sky-light-multiply', [128,0,0,255]);
  const fog = { ...draw('FogOfWar', 1, 0.5), detail: 2 };
  frame.draws = [draw('TerrainHard', 0), fog]; check('fog-texture-blending', [127,0,128,255]);
  const near = sprite('Cutout');
  const far = { ...sprite('Cutout'), texture: 0, model: identity() };
  near.model[14] = 0.25;
  far.model[14] = -0.25;
  // Separate camera depth from screen position to test reverse-Z occlusion.
  frame.camera.viewProjection = [1,0,0,0, 0,-1,0,0, 0,0,1,0, 0,0,0.5,1];
  for (const item of [near, far]) item.vertices = item.vertices.map((v, i, all) =>
    i % 11 === 1 ? all[i+1] : i % 11 === 2 ? 0 : v);
  frame.draws = [near, far]; check('object-reverse-depth', [0,0,255,255]);
  window.__probeResult = { status: 'pass', checks, colony: false };
} catch (error) {
  window.__probeResult = { status: 'fail', error: String(error), stack: error.stack };
} finally { renderer?.dispose(); }
document.querySelector('pre').textContent = JSON.stringify(window.__probeResult, null, 2);
