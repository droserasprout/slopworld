# Installed Waylith graphics probe

The colony terrain work is separate from the diagnostic renderer below.
`terrain_prepare.py` adapts the installed MapWorldUV/TerrainHard/TerrainFade/
TerrainFadeRough shader interfaces for WebGL. `browser-terrain-shaders` verifies
compilation, texture sampling and alpha blending. `terrain-renderer.js` consumes
the real mesh/material capture in `runtime/BrowserTerrain.cs`; `browser-terrain`
attempts that path with a browser-generated save. It reports other shader layers
and persistent sources as omitted, uses level-zero linear/repeat texture sampling,
and does not establish a full colony frame. The saved-colony check now passes:
78 terrain draws, eight decoded textures and 7,443 distinct pixel colors from
the actual map. The snapshot contained 456 commands in total; water, objects,
plants, lighting, shadows, fog and overlays remain explicitly omitted. The local
`artifacts/terrain.json` and screenshot record this partial view.

`browser-objects` extends capture to Cutout, CutoutComplex, Transparent and
CutoutPlant. The installed material resolver supplies uniform bytes and texture
bindings, including per-draw properties. The WebGL checks cover those shader
interfaces, alpha discard, blending, raw plant uniforms and reverse-depth ordering.
The Core-only stage rejects static atlas admission, selects direct pawn rendering
and retains CPU pixels after Apply. The saved-colony check passes with 698 draws
(78 terrain and 620 object/plant), 73 textures and 35,378 distinct pixel colors.
The 807-command snapshot includes 399 plant draws; all four supported object
shader families render, including per-draw overrides. `artifacts/objects.json`
and its screenshot retain the evidence. Six skin and three hair submissions,
water, lighting, shadows, fog and overlays remain omitted; this is a partial frame.

This directory owns a small browser renderer, a local shader adaptation step, and
read-only inspection of the installed engine's native boundary. It does not start
RimWorld, replace Waylith's renderer, or establish colony startup in a browser.
The supervising experiment owns the HTTP server, CDP harness and WASM integration.

Run from the repository root:

```sh
just --justfile experiments/browser/graphics/justfile prepare
just --justfile experiments/browser/graphics/justfile inspect
just --justfile experiments/browser/graphics/justfile test
just --justfile experiments/browser/graphics/justfile shader-targets
```

`RIMWORLD` overrides the installed game directory. `out/` is ignored and contains
locally derived shaders, a copied installed language-icon PNG, provenance hashes,
and metadata reports. These files must stay local. No game DLL is copied by this
component. `source` checks out the public BRUTAL source at commit
`7a3b72d84368872b885094e34fe8cdf5f7989460` under ignored `vendor/`.

## Browser integration

Serve `out/` at `/`, or serve `experiments/browser/` and open
`/graphics/out/index.html`. `window.__probeResult` moves from `status: "running"`
to `"pass"` or `"fail"`, with individual pixel checks and source SHA-256 hashes.
All seven checks pass in the supervisor's Chromium/SwiftShader harness,
recorded in `../artifacts/graphics.json`. The seventh check additionally
decodes and samples the installed `Data/Core/Languages/English/LangIcon.png`.
This is a shipped UI image, not a decoded colony asset pack.

Import `renderer.js` independently of the test page:

```js
import { createSceneDefaultRenderer, loadSceneShaders } from "./graphics/renderer.js";
const sources = await loadSceneShaders(new URL("./graphics/", location.href));
const renderer = createSceneDefaultRenderer(canvas, sources);
renderer.setTexture(bitmap); // ImageBitmap, or Uint8Array RGBA8 plus width/height
renderer.draw({ vertices, model, viewProjection, color, textureST, textured: true });
```

`vertices` is a `Float32Array` of complete triangles, 14 floats per vertex:
Position3, Normal3, Color4, UV4. Matrices are 16 floats in GLSL column-major order;
both default to identity. `color` defaults to `[1,1,1,1]`, `textureST` to
`[0,0,1,1]` (offset XY, scale ZW), `textured` to false. Draws clear the canvas
unless `clear: null` is passed, allowing subsequent submissions to accumulate.
Depth testing, culling and blending are disabled. This supports one
material/texture per draw; it is a verification boundary rather
than a scene compositor. `readPixel(x,y)` uses bottom-left coordinates;
`dispose()` is idempotent. The caller owns frame pacing and input.

`prepare.py` adapts only the installed `SceneDefault` pair: GLSL 450 becomes ES
300, Vulkan push constants and two descriptor uniform blocks become three
`std140` UBO bindings, the sampler becomes a texture-unit uniform, and clip Z
changes from Vulkan's 0..W interval to OpenGL's -W..W interval. It removes
explicit varying locations unsupported by ES 300. Unknown required constructs
fail preparation; the generated shaders must also pass browser compilation.
The probe checks material/vertex multiplication, texturing, alpha discard,
model/view transforms, negative clip-depth rejection and installed PNG sampling.
Full-scene orientation, color-space equivalence and performance remain untested.

## Measured engine boundary

`just browser-draws` exercises an initial draw-submission adapter. The managed
probe creates a real `Verse.MeshMakerPlanes` mesh, submits it twice through actual
`UnityEngine.Graphics.DrawMesh`, and consumes Waylith's `CaptureSnapshot`. The
encoder reads the installed CPU mesh arrays, indexed submesh and base vertex,
material color, and model transform. Original SceneDefault shader sources are
mounted in WASM for Waylith's real material-layout parser; adapted shaders run
in WebGL. Two separated red/green planes plus an empty center are pixel-checked,
and clearing submissions is checked for stale commands.

This supports only opaque, uninstanced SceneDefault triangle commands without
per-draw properties, vertex colors, queue overrides, or persistent draw sources. It rejects those unsupported features rather
than discarding them. It does not implement the engine render passes, camera/culling,
depth, blending, terrain generation, general texture residency, or colony frame loop. The test scene is explicitly
constructed by the probe, not a loaded colony. Evidence is `../artifacts/draws.json`.
`runtime/BrowserDraws.cs` owns snapshot conversion; the browser renderer owns GPU
submission. The Unity compatibility layer stores column-vector matrices inside
`System.Numerics.Matrix4x4`, so upload must extract columns (translation M14/M24/M34),
not assume the usual Numerics row-vector convention.

`just browser-textures` extends this path with the installed concrete terrain
texture. `runtime/PackedTexture.cs` reads the full local pack through `AssetPack`,
joins the split DDS via `ContentPack.TryReadTexture`, parses it with
`PackTextureSource`, and populates a real engine `Texture2D` without requesting a
native GPU allocation. The texture's CPU decoder expands BC7 to RGBA in WASM,
retaining the DDS swizzle and alpha metadata. The draw snapshot carries those
pixels to WebGL with its actual material/mesh. No host image conversion or native
rendering is used.

The measured texture is 512×512 BC7. Three distinct sampled texels match the
managed decoder's bytes exactly; both submitted meshes render and the empty gap
stays black. `../artifacts/textures.json` records dimensions, format and readbacks.
The sampler is explicitly set to point/clamp for this check. The adapter rejects
other sampler modes, non-identity texture transforms, unreadable textures and
non-opaque alpha. It uploads only level zero; native mip filtering, color-space
equivalence, transparency and general texture residency remain unfinished. This
still uses a probe-created scene, not a map or a complete engine frame.

`just browser-scene` connects the encoder to actual engine frame bookkeeping and
UnityWorld updates. Its MeshRendererDriver supplies draws during PostLateUpdate,
and browser animation callbacks own presentation. Visibility changes, transforms,
frame counters and shutdown are checked. This advances beyond direct DrawMesh
probe calls while remaining a test scene without RimWorld bootstrap or native
render passes; see [runtime lifecycle ownership](../runtime/README.md).

`inspect/` reads PE metadata, without loading game assemblies or executing their
initializers. `out/native.json` records assembly hashes, native imports, selected
Waylith types and signatures, and native-wrapper member references. These are
static references, not reachable-call counts or a runtime trace. Dynamically
resolved function tables are not exhausted by the P/Invoke count.

| Installed assembly | Declared native imports |
| --- | --- |
| Brutal.Glfw | 149 (`glfw`) |
| Brutal.Vulkan | 4 (`vulkan`), plus dynamically loaded function tables |
| Brutal.Vulkan.Vma | 73 (`vma`) |
| Brutal.Slang | 279 (`slang`) |
| Brutal.ImGui | 1,146 (`imgui`) |
| Brutal.Ktx / Gli / Stb | 89 / 22 / 17 |
| Waylith | 112, including 59 `waylithaudio` imports and OS/window calls |

Waylith contains 818 member references into Vulkan namespaces, 83 into GLFW,
and 24 into Slang in the inspected build. Its references include descriptor
sets, push constants, indexed and indirect draws, pipeline barriers, compute
dispatch, graphics/compute pipelines, VMA allocation, swapchains and presentation.
The apparent interfaces are already tied to Vulkan:

- `IRenderFrameState.GetDevice()` returns `Brutal.VulkanApi.Device`;
  `GetCommandPool()` returns `VkCommandPool`.
- `IRenderContext.TrySubmitFrame(...)` takes spans of `VkSemaphore` and
  `VkPipelineStageFlags`; `GetViewportState()` exposes a Vulkan struct pointer.
- `IRenderPass.Record(FrameContext&, Object)` receives a context whose accessors
  expose Vulkan command buffers, render passes, framebuffers and formats.
- `RuntimeShaderCompiler.CreateShaderModule` and `ComputeKernelCache` own native
  shader-module and pipeline construction. A browser shader compiler alone does
  not replace these owners.

The public [BRUTAL repository](https://github.com/ahwootech/BRUTAL) supplies the
bindings, not Waylith's renderer. Its
[`ImportResolver.cs`](https://github.com/ahwootech/BRUTAL/blob/7a3b72d84368872b885094e34fe8cdf5f7989460/Brutal.Vulkan/ImportResolver.cs)
loads an OS Vulkan library and obtains `vkGetInstanceProcAddr`; generated
instance/device tables then call unmanaged pointers. Its
[`Glfw.Vulkan.cs`](https://github.com/ahwootech/BRUTAL/blob/7a3b72d84368872b885094e34fe8cdf5f7989460/Brutal.Glfw/Manual/Libraries/Glfw.Vulkan.cs)
creates a native Vulkan surface. The inspected source snapshot has no project or
solution files, and its package naming differs from the installed build.
It is useful implementation evidence, not a drop-in source rebuild of this game.

Consequently, replacing the surface or selecting a different BRUTAL package
cannot supply a browser backend. A full port needs Waylith renderer changes (or
an extensive compatible implementation) covering those Vulkan-shaped interfaces,
resource lifetime, shaders, textures, native UI and browser event/frame handling.
That rewrite cannot be bounded to the available BRUTAL source alone.

## Shader and WebGPU evidence

The local inventory scans 242 GLSL/HLSL source/include files under engine and core
shader directories. `manifest.json` lists exact paths for push constants,
descriptor layouts, storage buffers, compute workgroups and includes. For example,
`Data/Core/Shaders/InstanceColors.glsl` uses a `std430` storage buffer, while
engine texture processing and GPU probing use compute shaders. These require
separate adaptations beyond this two-shader WebGL probe. The standard
[WebGL2 API](https://registry.khronos.org/webgl/specs/latest/2.0/) does not expose
the Vulkan compute/storage-buffer pipeline.

`shader-targets` runs the installed native Slang library in separate subprocesses,
compiling the original default shaders and `GpuProbe.comp.hlsl`. The latter's
`BandwidthProbeCS` entry compiled successfully to 1,740 bytes of SPIR-V and 890
bytes of WGSL. **That WGSL is not browser-ready:** its generated push-constant
uniform `pc_0` lacks `@group`/`@binding`, which the
[WGSL resource interface](https://www.w3.org/TR/WGSL/#resource-interface) requires.
The supervisor's `../webgpu/probe.js` supplies that remap at group 0, binding 2,
validates the shader, dispatches one workgroup, and verifies all 64 output values
in Chromium's SwiftShader WebGPU adapter. Reproduce with `just browser-webgpu`;
evidence is in `../artifacts/webgpu.json`. This is one compute-shader adaptation,
not a general push-constant mapper or Waylith backend.

Direct GLSL parsing through this legacy Slang C API reports `glsl module not
available`; the glslang passthrough SPIR-V control reports an internal compiler
error. The library's GLSL helper files are present. These results describe this
probe configuration, not a claim that the game's existing shader compiler fails
or that GLSL translation is impossible. Diagnostics are in
`out/shader-targets.json`. A general translation route must reproduce Waylith's
session setup or use another verified compiler before adapting the full corpus.
