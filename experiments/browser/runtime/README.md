# Managed browser runtime probe

This experiment loads the user's installed RimWorld 1.7 managed assemblies into
the .NET 10 Mono WebAssembly interpreter. Its Core-only path generates and advances
a real colony locally in Chromium. It does **not** yet provide a playable rendered
colony or a complete replacement for Waylith's native engine.

The installed SDK is sufficient: no SDK installation, workload, AOT compiler, or
streaming server is needed. `Probe.csproj` uses the Blazor WebAssembly SDK solely
to package the interpreter; JavaScript calls managed exports directly. NuGet
dependencies are pinned in `packages.lock.json` and restored in locked mode.

From the repository root:

```sh
just --justfile experiments/browser/runtime/justfile inspect
just --justfile experiments/browser/runtime/justfile stage
just browser-validate experiments/browser/runtime/out/wwwroot /index.html experiments/browser/runtime/out/browser.json
just --justfile experiments/browser/runtime/justfile report
just --justfile experiments/browser/runtime/justfile verify
```

Set `RIMWORLD` to another installation root if needed. The default is the Steam
installation under the current user's home. Each stage cleans its own output
root. All copied DLLs, local decompilation, reports, and tool installations are
ignored by Git. Do not publish the staged roots: they contain proprietary game
assemblies. Nothing modifies the installed game. The scanner follows managed
references, excludes framework assemblies supplied by the runtime, and records
absent installation references rather than inventing replacements. Each manifest
entry records both original and staged SHA-256 hashes.

## Browser interface

The static root is `out/wwwroot`, entry `/index.html`. The page publishes
`window.__probeResult` only when its complete report is ready, and exposes
`window.__managedProbe` with these exports:

- `LoadAssembly(Uint8Array)` loads installed managed PE bytes and returns identity.
- `Run()` returns JSON containing runtime identity, architecture, browser status,
  and independent checks with values or exception chains and stacks.
- `SceneGeometry()` returns JSON `{source,cells,vertices}`. It constructs 192 real
  `Verse.IntVec3` cells, reads their coordinates and squared lengths, then emits
  diagnostic quads. The 1,152 vertices use position3/normal3/color4/uv4 floats.
  Positions already fit clip space with identity matrices. This geometry is a
  visualization of game coordinate calculations, not game terrain or a save.
- `Deep()` returns independent engine constructor, Vulkan initializer, and audio
  initialization checks. The standalone page runs it with `?deep`; the integrated
  demo does not call it.

Run each startup mode in a fresh page/runtime. Failed static constructors stay
failed within a runtime; repeated `Run()` calls are not independent startup tests.
`Deep()` runs before other native calls to preserve the engine constructor's first
exception stack. A successful future full entry point could exit or run forever;
the Chromium harness bounds the diagnostic run.

## Measured result

On the installed game reporting version **1.7.5000**, using SDK **10.0.114**,
runtime **10.0.14**, and Chromium:

| Probe | Result |
| --- | --- |
| Managed dependency loading | 55 assemblies, zero load errors |
| `Verse.GenMath.PositiveMod(-7,5)` | `3` |
| `Verse.IntVec3(3,0,4).LengthHorizontalSquared` | `25` |
| `UnityEngine.Mathf.Sqrt(81)` | `9` |
| Emit IL, `CreateDelegate`, invoke | `42`; dynamic methods work in this interpreter |
| Anonymous memory map construction | Succeeds; shared/file-backed mapping is not established by this check |
| `Thread.Start()` | `PlatformNotSupportedException` in the default single-thread runtime |
| Original `RimWorldWaylith.Program.Main` | Console logger starts a thread and fails |
| `Brutal.GlfwApi.Glfw.Init` | `DllNotFoundException: glfw` |

The scanner reports `0Harmony`, `StbImageSharp`, and `StbImageWriteSharp` as absent
from the game root. They are not required for the successful computations. This
probe does not install mods, stage assets, or prove every managed dependency path.

## Bounded startup adaptations

```sh
just --justfile experiments/browser/runtime/justfile stage-adapted
just browser-validate experiments/browser/runtime/out/wwwroot-adapted '/index.html?deep' experiments/browser/runtime/out/browser-adapted.json
just --justfile experiments/browser/runtime/justfile report out/browser-adapted.json
just --justfile experiments/browser/runtime/justfile verify out/wwwroot-adapted out/browser-adapted.json
```

`stage-adapted` owns a separate static root. Its manifest identifies three changed
assemblies and its `game/adaptations.json` lists four changes:

1. Replace `Program.InitLogger` with `ret`, avoiding the console logger thread.
2. Change only `Application.platform`'s unsupported-platform arm to WebGLPlayer=17.
3. Make `Bootstrap.ResolvePersistentDataRoot` use the browser virtual temporary
   directory, avoiding Windows `shell32.dll` special-folder lookup.
4. Give `Brutal.Platform.CurrentFamily` diagnostic sentinel 3; all desktop OS
   predicates remain false. This bypass is not an implemented browser backend.

The patcher locates owners through metadata, preserves PE layout, and refuses
unknown platform-initializer shapes or unsafe method-body replacements. Only
ignored staged copies change. The separate `?skipLogger` mode on the baseline
root selects just the first adaptation for the intermediate failure: an
`Application` static initializer `NotImplementedException` on browser OS.

After all four adaptations, the real entry point reaches
`Waylith.EngineApplication.Run` and fails at
`ProcessLifetime.Hook → Console.CancelKeyPress → PosixSignalRegistration.Create`
with `PlatformNotSupportedException`. The direct **actual engine constructor**
reaches `Glfw`'s native loader and fails with `DllNotFoundException: glfw`.
Independent native checks also reproduce Vulkan's `NullReferenceException:
Failed to load Vulkan library` and `MiniAudioBackend.Initialize(true)`'s
`DllNotFoundException: waylithaudio` (even its no-audio mode needs the library).

These are concrete missing platform capabilities. A playable port needs browser
lifecycle/input/rendering/audio/storage implementations and compatible native
bindings or replacements; merely interpreting the installed IL does not supply
them. Multithreaded WASM was not validated here. Supporting threads would not
supply desktop GLFW/Vulkan/audio libraries or POSIX process signals.

## Headless bootstrap and installed assets

`just browser-lifecycle` runs the actual headless engine constructor (default null
audio backend), invokes `RimWorldWaylith.Bootstrap.Initialize`, and disposes the
engine twice. The four-adaptation root fails in `ConfigureRuntime`: the browser
has no native executable directory. A separate `out/wwwroot-lifecycle` root adds
one bounded IL adaptation, replacing that directory expression with
`AppContext.BaseDirectory`. It then reaches the real missing `/Assets.zip` check.
The regular baseline and four-adaptation roots retain their original behavior.

`just browser-assets` copies the installed `Assets.zip` into the ignored lifecycle
root, records source/copy SHA-256 hashes in `out/asset-hashes.txt`, and transfers it
in chunks into the browser virtual filesystem. Incomplete transfers are discarded;
the completed file is session-local, not persistent storage. The installed pack
used here is 355,294,607 bytes. Waylith's actual `AssetPack.TryOpen` successfully
memory maps it and reads 6,132 entries, including its content manifest. Bootstrap
then passes asset validation and fails at `WaylithJobRuntime..cctor → Thread.Start`.
This is evidence for the real file-backed asset reader, not just anonymous mmap.
Native asset codecs and all payload types have not been exercised.

`BrowserLifecycle.cs` owns this checkpoint and the asset transfer. It does not
start a browser frame loop, render, load Core definitions, or create a colony.
The next headless startup boundary is job scheduling; graphics still requires
a browser backend independently of that failure. Reports live in
`../artifacts/lifecycle*.json`. Recipe success means the expected checkpoint was
verified, not successful game startup.

## Serial job scheduling

`just browser-jobs` stages a separate `out/wwwroot-jobs` root with the lifecycle
path adaptation and a serial scheduling adaptation in `Scanner/BrowserJobsPatch.cs`.
It changes only staged `Waylith.dll`: the initializer creates zero background
threads, submission executes each chunk through the original `HandleState.Execute`,
and `WorkerCount` reports one execution lane for batch sizing. The real worker
array stays empty, so original shutdown does not join fictional threads. Original
dependency bookkeeping, allocation scopes, completion, faults, and cleanup remain
in the installed implementation. The patch refuses unexpected inspected IL layouts.

The browser checks execute dependent jobs, repeated completion, 37 chunked
iterations, nested jobs, and failure/recovery. Single-job faults still rethrow at
`Complete`; as in the installed implementation, dependents of a faulted job can
run. This is synchronous execution, not background concurrency or a yielding
scheduler: large jobs can block the browser and nested submission can recurse.

With this adaptation and the installed asset pack, the actual bootstrap reaches
`PrepatcherCompat.Install` and fails resolving `WaylithClr.FieldInjectionBatch`
from stock browser `System.Runtime`. The installed injection layer unconditionally
declares dictionaries using this custom CLR type. It requires a compatibility
implementation or offline transformation, not another worker-thread bypass.
Evidence is `../artifacts/jobs.json`; no rendered frame or colony is established.

## Browser-paced engine scene

`just browser-scene` uses the serial-job stage to construct the actual headless
`EngineApplication` and a real `UnityRuntime`. A probe-created GameObject has the
installed MeshFilter and MeshRenderer components and a mesh from RimWorld's own
mesh factory. JavaScript requestAnimationFrame drives
`EngineApplication.BeginFrame → UnityRuntime.BeginFrame/Update → EngineApplication.EndFrame`.
UnityWorld's installed PostLateUpdate renderer driver generates the submissions;
the frame adapter does not directly call Graphics.DrawMesh. The narrow browser
encoder consumes the resulting snapshot and WebGL draws it.

`BrowserScene.cs` owns this test scene and frame/shutdown handoff; `wwwroot/scene.js`
owns browser pacing and presentation. `browser-scene` checks three frames: visible
on the left, disabled with zero submissions, then visible on the right. Unity
frame counts are 1/2/3 and engine counts before EndFrame are 0/1/2. It also checks
repeated shutdown and rejection of frames after shutdown. Restart is rejected:
installed renderer-driver subscriptions and job shutdown currently require a fresh
WASM runtime. Evidence is `../artifacts/scene.json`.

This is an engine-managed test scene through a narrow replacement rendering path.
It does not run RimWorld Bootstrap/Entry, native Vulkan render passes, Core loading,
or colony simulation. The scene controls are test inputs, not game AI or physics.
The jobs-stage bootstrap still stops at custom CLR field-injection initialization;
the guarded Core-only stage below deliberately has a narrower admission contract.

## Core-only Entry startup

`just browser-entry` builds a separate `out/wwwroot-core`. `BrowserEntry.cs` mounts
the installed Core data and shader sources into the temporary browser filesystem,
constructs the engine with an explicit silent mixer, and runs the real
`Bootstrap.Initialize` and `OnUpdate` callbacks. The original Entry scene builder
creates `Root_Entry`; this is separate from the mesh test scene. No graphical
game UI or colony rendering is supplied by this checkpoint.

`compat/Offline/CoreBootstrap.cs` transforms only the staged assemblies. The three
custom-injection bootstrap callbacks become guards: Core files are required,
additional data directories and Mods contents are rejected, and the mod-assembly
callback rejects every nonempty assembly list. This does not implement custom CLR
field injection or make SlopWorld startup compatible. The jobs/injection failure
baseline remains separately reproducible.

Cache writes use the installed atomic writer serially, retaining its best-effort
IO exception policy. XML loading uses the original calling-thread work-bag drain
with zero worker threads. Long events use the existing synchronous executor while
retaining their queue, error, completion, and scene-change handling. These changes
can occupy the browser thread during startup; the harness permits a longer
diagnostic timeout and records coarse loader stages. Desktop Steam initialization
is explicitly unavailable; it remains uninitialized rather than invoking a native
library and requesting application quit.

`GenThreading.ParallelForEach` also needs adaptation: its thread-pool enqueue plus
blocking wait stalls cross-reference resolution in a single-thread browser.
`Adapters/SerialWork.cs` takes the same input snapshot, invokes every callback
serially, and retains per-item exception logging and continuation. The browser
check covers source-list mutation, failure, and subsequent work. The engine's
`WAYLITH_NO_ASSET_PREFETCH=1` switch selects on-demand loading without prefetch
threads; it does not replace assets with dummy data.

`Adapters/` compiles against local game interfaces without copying binaries into
tracked paths. Its silent backend supplies mixer controls only and throws for
sound decoding/playback. Its logger supplies a synchronous provider without the
desktop console worker. Engine errors must be inspected alongside frame return
values: UnityWorld catches component exceptions. The report includes loaded state,
definition counts and world diagnostics, and the harness preserves console events
even on timeout. Evidence stays in `../artifacts/entry.json`.

The installed 1.7.5000 run reaches `PlayDataLoader.Loaded=true` with 1,448 ThingDefs,
124 TerrainDefs and 190 RecipeDefs. It runs five real Entry updates. This remains
incomplete startup: audio decoding/playback, the language-icon PNG loader, and custom
CLR profiler instrumentation report errors. The browser retains its asset-pack
mapping and skips the desktop OS resident-page hint. The silent mixer is not audio
support. Repeated identical engine errors
are deduplicated in the report, with total and omitted-error counts kept explicit.
Neither `Loaded=true` nor an initialized Entry scene establishes a playable colony.

## New-game path

`just browser-play` continues from Core loading into the installed Play scene.
`Adapters/GameSession.cs` requests that scene and observes actual game state; the
installed `Root_Play` owns quick-test setup, world generation, Crashlanded scenario
preparation and `Game.InitNewGame`. Default planet coverage and 250×250 map size
remain in the game code. Generated saves are not read from or written to the host.

The staged range helper dispatches the original `GenThreading.ParallelFor` batches
inline, retaining their completion counters and per-callback handling. Diagnostics
trace planet subdivision, world tiles and map steps during synchronous startup.
The browser then requests normal game speed and samples maps, colonists, pawn jobs
and tick counts. Its verifier requires a generated map and advancing simulation;
merely reaching Play or constructing a map does not pass. This path does not yet
provide the browser renderer or game UI. Reports remain local in
`../artifacts/play.json`.

The measured run generated seed `zucchini`, 119,904 world tiles and a 250×250 map
containing 38,890 things. Three colonists acquired wander jobs and moved while
ticks advanced from 0 to 308. An audio-related music error remains visible after
the simulation update; the sampled ticks continue advancing. No colony pixels
were rendered by that check.

`Adapters/GameStorage.cs` delegates serialization and loading to the installed
`GameDataSaveLoader`. `browser-save` exports an experiment-owned `.rws` from the
browser VFS through CDP into ignored artifacts; `browser-load` stages it into a
fresh runtime and checks restored seed, pawn identities and tick count before
requiring further simulation. This transfer is not persistent browser storage.
Save/load validation is separate from the established new-game simulation check.
The measured round trip exported 13,606,260 bytes for seed `backgammon`, restored
the three pawn IDs and tick 308 in a fresh runtime, then advanced to tick 614 with
pawn movement. `artifacts/load.json` preserves that result. Its terrain attempt
had zero terrain draws; successful restoration alone is not rendering evidence.

The terrain path configures the browser viewport and refreshes the game's UI
scale before map updates. This supplies the dimensions used by section culling,
which native OnGUI normally initializes. With that handoff, the saved colony
submits 456 commands, including 78 supported terrain commands. WebGL renders
those using eight real decoded textures. `artifacts/terrain.json` records the
partial rendering check and retained save identity; unsupported layers remain
listed rather than silently treated as rendered.

The object extension captures installed material-resolver uniform bytes and texture
bindings through `Adapters/MaterialCapture.cs`. The Core stage disables static
atlas admission and selects the pawn renderer's existing `disableCache` path;
native atlas baking requires unavailable GPU render targets. It also retains CPU
texture pixels after `Apply`, since headless uploads cannot preserve procedural
textures. A startup check exercises actual solid-texture creation and verifies all
RGBA channels before loading Core. `artifacts/objects.json` verifies save identity,
advancing simulation and 620 object/plant draws alongside 78 terrain draws. Skin,
hair and other unsupported layers remain explicitly omitted.

## Browser input adapter

`just browser-input` stages the adapted runtime and runs synthetic DOM events
through `wwwroot/input.js`, `BrowserInput.cs`, and the installed input owners.
`attachInput(canvas, managed)` returns `frame()` and idempotent `dispose()`;
the future browser frame owner must call `frame()` before game Update. It clears
transient state and drains queued events once per frame. No animation loop or
engine rendering is supplied by this adapter.

Waylith and RimWorld have **separate** native input bridges. The adapter feeds
both `Waylith.Input.InputState` and `UnityEngine.InputSystem.InputSystem` using
their real methods. Mouse positions are framebuffer-scaled, top-left for Waylith
and bottom-left for Unity. DOM middle/right button indices are remapped. Wheel
deltas use a provisional 100 pixels / 3 lines per notch convention. Key repeats
reach Unity's repeat path without producing new Waylith presses. Focus loss,
pointer cancellation, queue overflow and disposal reset held input.

The browser check verifies press/hold/release through both installed owners,
mouse coordinates, right-button mapping, wheel, focus loss, and repeated cleanup.
This does not establish playability or touch/gamepad support. The Entry workspace
adds text/IME and native rebindable fullscreen intent; its DOM fullscreen boundary
retains user activation and reports actual state, including browser/Escape exits.

## Inspection commands

```sh
just --justfile experiments/browser/runtime/justfile scan
just --justfile experiments/browser/runtime/justfile disassembler
just --justfile experiments/browser/runtime/justfile entry-source
just --justfile experiments/browser/runtime/justfile source UnityStubs UnityEngine.Application out/application.cs
just --justfile experiments/browser/runtime/justfile source Waylith Waylith.EngineApplication out/engine.cs
```

`Scanner/` owns metadata inspection, dependency staging, and bounded local patches;
`Program.cs` owns the managed probes and exported contract; `wwwroot/main.js` owns
fetching and report publication. The parent experiment owns HTTP serving, CDP
validation, the WebGL renderer, and integrated application. Official runtime
background: [JSExport browser interop](https://learn.microsoft.com/en-us/aspnet/core/client-side/dotnet-interop/wasm-browser-app?view=aspnetcore-10.0)
and [experimental WASM threading](https://github.com/dotnet/runtime/blob/release/10.0/src/mono/wasm/threads.md).
