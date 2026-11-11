# Browser prototype boundary

[`experiments/browser/`](../experiments/browser/README.md) owns the local-only
Waylith/RimWorld WASM investigation. `just/browser.just` exposes its checks.
Production mod startup and daemon runtime remain independent. `slopweb/` owns the
installed launcher; `tools/release/slopweb.py` stages redistributable tools and mod
UI content. Local game preparation belongs to the launcher, never package staging.
Installed assemblies, derived assets, saves, and reports stay in ignored output.
Prototype `copy-eco` rebuilds preserve explicitly prepared fonts, background frames,
portrait snapshots and reference appearance through `refresh_prepared.py`; other
old files are replaced. Refresh those snapshots explicitly when their native source
changes. Production launcher preparation still creates a fresh immutable runtime.

`slopweb/aot.py` owns shared mixed-AOT project generation, publish-name loading,
and the installed launcher's compilation cache/runtime composition. The separate
`aot/` experiment delegates compiler-input construction to that owner. The
launcher's interpreted preparation remains the owner of final patched game IL and
current private assets. AOT compilation keys exclude appearance and browser assets;
composition joins those current assets with a successfully published framework.
A stable per-compilation lock spans candidate creation, publish and composition,
and is inherited by the compiler so interruption cannot admit another writer.
Release staging allowlists Probe's authored project and declared sources, never
private compiler directories; changes to its compile list require package review.

The optional `nativeFonts` workspace parameter loads `assets/fonts/manifest.json`
from the local prepared root. `runtime/justfile`'s `font-pack` recipe bakes a selected
font's three UI tiers using the installed ImGui/FreeType implementation; run it
after staging, which replaces the output directory. Font bytes and derived atlases
remain local. BrowserFontBank owns coverage, metrics, atlas pixels and mounted paths;
the mod's NativeFonts/UiFont retain family selection and style assignment. The host
reference preparation calls the actual mod's NativeFonts catalog and FontconfigFonts
resolver, mounts the selected terminal face, and passes family order to the staged
fontconfig boundary. WASM never invokes host fontconfig directly.
The installed launcher's private preparation uses the same FontProbe for native
UI candidate selection, terminal family resolution and glyph baking. Package staging
allowlists the helper executable and metadata because its build directory also
contains installed game references; those references must never be bundled.
Private font preparation uses native settings defaults when no profile value exists;
an unavailable saved UI family falls back to Auto in the private startup snapshot
without changing the desktop profile. Failure to resolve Auto still aborts preparation.
an explicit UI size zero retains the original 11/13/15 tiers. A mismatched
terminal bake size forces ordinary text through the slow fallback renderer.
It bakes normal, bold, italic and bold-italic variants of the selected UI and
terminal files through the native loader at densities 1, 5/3 and 2.
Startup and on-demand preparation share the UI size policy in `slopweb/fonts.py`:
profile tiers retain all four styles; authored fixed UI sizes 14, 22 and 28 also
receive normal faces independently of the profile. Overlapping sizes reuse their
tier bakes. These extra UI faces do not enlarge terminal bakes.
Rendering-mode flags are distinct from style flags; the native bold/oblique
constants extend the normal rendering mode. `browser-font-styles-check` compares
actual ink, line metrics and italic advances across all prepared variants.
Style is part of face lookup, so styled runs retain family and native metrics.
`BrowserFontBank` keeps font indices stable while selecting the nearest
prepared density; viewport changes invalidate its bounded face lookup cache and
advance atlas revision. Returning to a density reuses its texture. Other densities
still resample. `just browser-host-font-bakes` refreshes the prototype through the
staged private-preparation helper, and `browser-host-font-density-check` verifies
selection, invalidation and texture reuse in a running diagnostic browser.
`slopweb/fonts.py` owns on-demand host baking; `fonts.mjs` validates same-origin
requests and serializes subprocesses. The launcher supplies host-only game/cache
context after interpreted or AOT preparation; neither paths nor credentials enter
font responses. The host catalog includes installed families and monospace flags
without mounting every font. Native settings dirty events notify the browser,
which debounces changes and keeps one request active with only the newest pending
selection. Startup seeds this cache from already mounted fonts. Display resize
requests only the nearest prepared density; drawing never starts discovery or I/O.
Successful installs rescan mounted files and reapply UI/terminal owners before waking
the workspace. Old usable faces remain while work runs. Superseded responses do
not publish; returning to an installed selection restores its family chain.

Font state is bounded independently: 64 atomic private disk entries per serving
runtime, eight host responses (including short-lived failures), 1024 native atlas
variants, and 384 MiB of retained RGBA atlas pixels. Admission checks reject a new
selection before installation when its conservative allocation exceeds that budget;
existing native styles and packets keep their textures. Identical variants reuse
indices/textures. Native face hits and misses are cached, and temporary size
registrations are bounded. Failure restores the previous settings, displays a retry
message, and never retries from a frame; an explicit click can retry a failed host
operation. Successful selection clears that message. Font coverage remains the
FontProbe scalar set: U+0020–U+00FF, U+0400–U+052F and U+2000–U+27FF, filtered
through actual face coverage. Other characters and unprepared rich-text sizes retain
the diagnostic fallback. Font files changed on the host require a rebuilt private
runtime to refresh its font cache.

Plain covered runs use baked glyphs at matching density. Prepared coverage
includes Cyrillic U+0400–U+052F; an omitted scalar otherwise takes the
approximate text path even when the host font contains it. The benchmark's
`cyrillic` phase echoes Russian text in owned terminals and rejects fallback draws.
Prepared wrapped text and rich color spans use the installed `TextLayout` through bounded
font-access hooks, then share the baked glyph emitter. The layout/parser/wrapping
algorithms remain in the installed assembly. Sizes and characters absent
from the prepared bank still use the approximate diagnostic renderer; this does
not establish visual parity or complete terminal font support.
Terminal line boxes accommodate the native rounded first-line height as well as
fractional spacing, so row admission does not clip descenders.
Covered single-line editing uses the same glyph advances and device-pixel rounding
for caret hit testing and selection. Caret tables are allocated lazily within the
bounded text-run cache and replaced when display scale changes. Prepared multiline,
wrapped and rich editing uses the installed layout's hit testing and caret positions;
selection follows its line geometry. Unsupported coverage still uses the diagnostic fallback.
Plain-run measurement and horizontal alignment use per-glyph rounded advances too;
rounding only the final total can disagree with the rendered endpoint.
The opt-in projected-glyph cache lets each bounded plain-text cache line retain
at most one projected glyph run. It stays off by default: real idle/output
comparisons have not established a frame-time benefit. Its
identity includes pen/origin, scale, the complete transform and scroll offset,
fine text clipping and the current physical drawing clip. Reuse skips repeated
glyph projection/culling but still validates image uploads and emits the original
ordered, size-bounded batches with the current tint. Runs shorter than eight glyphs
retain direct emission. Text-run eviction releases projected geometry too;
`WorkspaceGlyphCache` exposes the adapter diagnostic comparison switch to WASM. The CPU-only
`NativeTextTests` check compares real adapter packets with reuse disabled/enabled;
its `benchmark-glyphs` recipe measures stable and moving labels without starting
an engine or browser. Native CPU timings do not establish WASM or workload latency.
Baked runs may stop at a fine text clip or nested drawing clip only when immutable
metrics prove nonnegative advances and include conservative negative overhang.
Drawing-tail bounds replay the actual GUI/device round trip and retain the reference
path for unsafe metrics, nonfinite coordinate ranges and transformed text. Empty
drawing scopes can reject whole runs independently of their advances. Skipped tails
still count fine-clip-admitted glyphs for diagnostic traces and projected-cache replay;
ordinary unprofiled drawing avoids that traversal. Culling is enabled by default;
`WorkspaceGlyphTailClip(false)` selects the reference path for packet and headless
workload comparisons. Faded-label slices
narrow drawing clips through nested groups while retaining full-span fine text clips.
Single covered ASCII labels bypass text-run caching and line traversal in the browser
adapter. They retain independently rounded label origins, native face/style/density
selection and the same glyph clipping/upload rules. Other text keeps the general
layout path; terminal columns, sprites and input remain mod-owned.
`WorkspaceCellFastPath` exposes a diagnostic reference switch. `NativeTextTests`
compares actual serialized draw packets and uploaded RGBA bytes across both paths;
`benchmark-glyphs` includes thousands of single-cell labels with stable and moving
positions. This CPU evidence does not establish browser frame-time or screenshot parity.
Covered axis-aligned text uses the installed renderer's glyph-quad clipping rules,
including ascender overhang and first-line bottom clipping. It does not add a Canvas
clip scope per label. BrowserCommandState suppresses unchanged serialized transforms
while tracking clip save/restore; native group coordinates and hit testing are untouched.
The drawing clip stack is separate from hit testing. Fully clipped image quads are
discarded before upload/serialization, including repeated native faded-label slices.
Partially visible quads retain their original geometry and UVs for Canvas clipping;
render targets inherit the same physical clips. The GUI probe checks empty nested
clips, later visibility/upload, and restoration.

`Probe.WorkspaceObserveLabels(bool)` opts into read-only DrawText observations and
returns the previous setting; it defaults off. `WorkspaceLabels()` returns JSON
`[{text,x,y,width,height}]` from the latest repaint, independent of fallback text
commands or native-font glyph batches. Coordinates are physical screen bounding
rectangles intersected with drawing/fine clips and screen bounds, not glyph ink
or hit-test rectangles. Native glyph admission rejects fully culled text; offscreen
render-target draws, whitespace, transparent labels and input/layout passes are
excluded. Reading does not drain or repaint. Every repaint clears the snapshot,
including an empty repaint. Retention keeps the latest 4096 calls under 262144 UTF-16
units, truncating each text to 4096 units, so later dialogs survive terminal cell
traffic. Disabling releases retained text. The observations do not establish
occlusion visibility when a later control paints over an earlier label.
`WorkspaceSidebarRows()` adds a body-clipped `rect` in native UI coordinates to
each row's session identity. It applies `Sidebar.AgentContentOffset` to the native
content-local row; callers apply UI scale before dispatching physical pointer input.
This avoids treating mutable or duplicate host titles as session identities.

The workspace starts in loaded Entry without creating a game, world, or map.
`WorkspaceDrawing.RequireEntry` checks that boundary at startup and every update;
workspace URLs reject play/load/save flags. Ordinary agent sessions use actual
sidebar session-only rows without synthesized colonists. Colony probes remain
separate. [Eco](mod-eco.md) owns pause behavior where a colony exists.

## Host and transport

The browser executes installed game code and the actual mod locally. `slopd`
retains process, tmux, sandbox, terminal, and credential ownership.
The loopback server owns same-origin `/host/api/` and `/host/ws` bridges. It injects
host credentials upstream, rejects foreign origins, and preserves protobuf bodies
and WebSocket framing/backpressure. Credentials never enter browser assets or URLs.
An agent token cannot authorize root-only session/configuration routes; the bridge
does not elevate that identity.

BrowserHttpPatch adapts staged HTTP construction/base URL. Both ConnectionInfo and
DaemonClient base-URL getters use BrowserTransport directly; shared config reads
must not resolve the native endpoint descriptor before returning the bridge URL.
The host bridge owns endpoint credentials and native descriptor discovery is unchanged.
BrowserSocketPatch
substitutes asynchronous browser WebSockets for raw sockets. Actual DaemonClient
admission/deadlines/completion pumping, HubTransport serial checks/bounded queues,
SessionHub catalogs, and TerminalIO subscriptions retain ownership. Overload closes
the socket rather than blocking the browser thread. Shutdown flush reports false
while a write is pending. [Client](mod-client.md) and [wire](protocol-wire.md)
contracts still apply.

`browser-host-reconnect-check` reloads a retained diagnostic page with temporary
socket tracking, then blocks page HTTP and closes its transport twice. It checks
native offline/online state, continued offline frames, and fresh DOM-input echoes
through recovered subscriptions in invocation-owned fixtures. Cleanup restores
network access, the original selection and socket constructor, and deletes the
fixtures. The host daemon is never stopped; daemon restart and run replacement
are separate recovery cases.

BrowserTerminalSize admits resize requests only when local requested geometry,
the session run, or transport generation changes. Screen dimensions can reflect
another client's legitimate resize; they are not an acknowledgment to retry
forever. This browser boundary prevents fights with an already-running desktop
client without changing the daemon or requiring that game client to restart.

## Entry workspace and GUI

`WorkspaceDrawing` explicitly invokes shared `UiBubble` chrome after queued GUI
windows. Offline WASM installs selected hooks, so the native Harmony post-window
patch alone cannot present bubbles there. The door check exercises Advice expiry
and the Hint sequence through DOM clicks without creating a colony.

`bootstrap.js` paints the pre-runtime loading screen before importing the WASM
entry module. Its authored LoadingFont atlas is staged separately from Content.zip
and included in redistributable tooling; it contains no game-derived content.
The first workspace frame removes that screen, and startup failures reveal the
diagnostic error. A bounded timer permits startup in tabs with suspended animation
callbacks. This currently presents loading status with native glyph geometry;
the scrolling quote stream uses the authored shared quote pool, native line-fit
selection/history, word budget, trailing-word colors and half-line scroll hold.
`BackgroundProbe` resolves the installed
BGPlanet pack source and invokes the actual mod cache-key policy without starting
an engine. `just browser-host-background` copies only the matching frame set into
ignored local assets, with native preset/timing/layer parameters. A cache miss is
explicit; cold baking is not implemented. These derived frames never enter the
release bundle. Installed private preparation invokes the same exporter and ships
only its executable/metadata. Run installed preparation on the host when checking
the desktop cache: a sandbox may have different cache and temporary directories.
Early Canvas playback implements the native onset/walk/layer rules,
but browser JPEG decoding versus native BC1 playback and random timeline parity
remain unverified. It decodes a complete set before publishing and cancels pending
loads/releases ImageBitmaps on workspace handoff.

`ClientPlatform.IsBrowser` identifies the client independently of the daemon's host
or sidecar runtime. Browser staging replaces that capability getter; General shows
slopweb and a checked, locked Eco control because only Entry runs here.
`BrowserWindowPatch` redirects the existing fullscreen setter while keeping native
settings, palette and rebindable hotkey intent. `workspace-fullscreen.js` owns the
DOM Fullscreen API and reports actual state back to native settings, including
Escape and rejected requests. Only keys matching the live native fullscreen slots
dispatch synchronously in their DOM keydown event, including printable rebinds.
Ordinary text remains batched; slot edits and resets take effect before saving. Fullscreen is not persisted or
restored automatically in the browser. `just browser-test-window` checks that bridge;
`just browser-window-check` exercises trusted key admission and browser-owned exit
in an isolated Chromium document without loading the game.

BrowserWorkspace pumps engine time, client completions, SessionHub, and the real
WindowStack without colony updates. Waylith's deferred GUI window input/drawing
callbacks remain necessary. Never invoke the baseline executable-entry diagnostic
after workspace startup: it resets engine globals. Native rejection messages draw
after deferred windows so the terminal cannot cover them.
The custom Entry traversal also pumps native tooltips before window traversal:
the tooltip handler owns hover delay and expiry, and its ImmediateWindows must
join the deferred window drawing pass. Input regression checks exercise this
ordering and stationary-pointer repaints without a game or browser.

`BrowserUnderlyingWorkspace` dispatches native TopBar, sidebar and selected-session
gizmos when the fullscreen WorkspaceWindow is absent. SessionSelectable and the
installed GizmoGridDrawer retain command eligibility, rendering and execution;
MapInterface and map selection never run. Deferred window input precedes this layer,
and absorbing dialogs block its hotkeys. Browser background playback uses that same
window-presence boundary behind GUI commands. Native Screensaver requires a Game
and cannot activate in Entry; this adapter does not synthesize one.

The root URL and `workspace.html` use `responsive`: physical framebuffer dimensions
follow the browser viewport and DPR, including resize and display-density changes.
The plain root applies workspace defaults internally and retains `/` in the address
bar; it does not redirect or append query parameters.
Explicit reference probes use fixed `width`, `height`, and `uiScale` parameters;
without `responsive`, omitted values retain 1280x720 at scale 1. Browser staging
bypasses desktop `ResolveUIScale` inference because the browser supplies the complete
physical scale. Browser DPR and CSS viewport remain independent comparison inputs.
The host reference
recipe imports only allowlisted presentation preferences into the browser's memory;
it does not copy an entire profile or change the host settings.
Prototype server/probe startup refreshes that snapshot through the same allowlist as
the installed launcher. `just browser-host-appearance` refreshes an existing server's
snapshot; reload its browser page to apply it. Installed slopweb serves selected font
changes through its on-demand host baker; standalone prototype servers require
matching prepared font assets.
The app workspace overlays a versioned, origin-local presentation snapshot after
that host seed. `BrowserPreferences` owns the explicit scalar allowlist; staged
`ProfileSettings.Write` redirects to this sink while native dirty timing remains.
Entry pumps the native deferred save, and page hiding flushes pending presentation
values before reload. Unavailable storage and malformed values retain defaults.
Credentials, daemon configuration, commands and `hostHome` never enter this snapshot;
it does not write the host profile. Explicit diagnostic workspaces stay ephemeral;
`localPreferences=off` also disables the app snapshot for reference comparisons.
`BrowserKeyBindings` separately persists both slots of only the definitions loaded
from the authored `Defs/KeyBindings.xml`, under an origin-local versioned key.
Restoration waits until `BrowserModKeys` finishes native `KeyPrefs.Init`; the initial
native save cannot overwrite the pending snapshot. Native slot setters, conflict
validation, reset-to-defaults and `KeyPrefs.Save` retain ownership. A staged save
hook publishes the authored subset after native saving; no XML, unresolved game
bindings, host profile or arbitrary definition is copied into browser storage.
Malformed/unknown codes retain defaults, explicit `None` clears a slot, and saving
a reset replaces previous customizations. Diagnostic workspaces and
`localPreferences=off` leave this persistence disabled too.
`just browser-test-keybindings` checks origin storage, installed managed slots/reset,
and the staged save hook without starting the game or browser.
Prepared font assets still belong to the host and are not rebaked by local changes.
`browser-host-preferences-check` uses F9 in a retained diagnostic browser, waits for
the native debounced save, reloads, checks restored native state, and restores the
original layout. It does not send terminal text or change the host profile.
Any requested initial
session is resolved before opening a terminal, so startup does not temporarily
attach or resize an unrelated session. With no explicit session, an authoritative
empty catalog renders the underlying native sidebar and TopBar immediately; a
connection deadline also falls back to that UI, preserving the normal reconnect
pump. An explicit missing session remains a startup error.
`just browser-test-startup` checks these admission paths and underlying UI dispatch.
The root defaults never name a host session. `just browser-host-offline-startup-check`
verifies the prepared runtime with an unconfigured bridge, without stopping the
host daemon, and requires visible interactive chrome and continued Entry updates.
Captures must match physical dimensions and effective scale before a pixel
comparison is meaningful; matching dimensions alone does not prove parity.
`just browser-native-window-reference` captures only the existing X11 game window,
verifies its process ownership, and records its geometry beside the image. It does
not focus the window or send input. Multiple games/windows are rejected rather than
guessing a target. The desktop portal recipe is separate and can include unrelated
applications; prefer the window-only recipe while the desktop is in use. A captured
window's dimensions do not establish its effective UI scale or synchronized content.
`browser-host-workspace-bounded SESSION CSS_WIDTH CSS_HEIGHT DPR` controls reference
geometry under the diagnostic CPU bound. Use `browser-host-canvas-reference PROFILE
OUTPUT` for unscaled framebuffer pixels: the ordinary CDP page screenshot can be in
CSS pixels even when the canvas has a larger physical framebuffer. The comparison
recipe deliberately rejects differing image sizes rather than resampling either.

Native `GameCursor` owns cursor appearance, animation and hotspots. Browser staging
redirects its CPU readback and final Unity cursor call. Browser cursor images use
the native pixels with a CUR-embedded physical hotspot and density-aware CSS
image-set, so movement does not depend on page overlay updates. A pointer-transparent
overlay remains at viewport edges, where Chromium rejects custom cursor images,
and on browsers without this CSS support. Retained frames are bounded. Loading
transfers its decoded background frames to the workspace presenter, which disposes them on exit.

EngineApplication and UnityRuntime maintain separate clocks. Advance UnityRuntime's
BeginFrame explicitly without its world Update: frozen Unity time also freezes
sidebar layout snapshots, cache pruning, and input timing. Deferred sidebar layout
changes run before GUI dispatch against the next frame's geometry.

`compat/Offline/` applies bounded hooks to isolated copies with manifests; this is
not general Harmony support. Actual ModEntry/profile loading remains, but desktop
EarlyBootstrap and full ModBootstrap do not run. The workspace explicitly installs
actual mod options categories; BrowserOptionsPatch connects their row/page/footer
owners to Dialog_Options. Simulation definitions and unadapted hooks remain outside
this path.

BrowserSidebarPatch dispatches actual sidebar passes with empty pawn entries and
null map selection in Entry. Its cwd-title formatter receives the host home path
from private appearance preparation, solely for native `~` abbreviation. This
does not change WASM process HOME or mount host files. Ordinary metadata joins session-only row eligibility
without changing SessionInfo or wire data. BrowserModContent mounts authored
textures/themes after Core startup using the real ModContentPack/content holder.
Host PNG decoding supplies readable Unity pixels; ContentFinder retains lookup.
Ordinary session-only agent rows share native agent geometry and draw the existing
worker icon through `SessionPortraits`. `BrowserPortraits` binds that shared icon;
installed preparation neither requests nor imports per-session portrait snapshots.
Native pawn appearance remains pawn-owned. Native portrait export tools are explicit
diagnostics and do not supply the browser workspace.

BrowserWorkspaceDoors loads the authored PlannerBench
definition and binds its native ModDefOf field so TopBar can draw and toggle the
actual Planner view. It also binds authored Jukebox and Core's computer definitions,
without creating a building instance. `BrowserWorkspaceDoorsPatch` redirects only
TopBar's presence checks and Core menu action; map presence and arrival retain their
native contracts. Core's native Entry menu supplies Hint/Advice dialogs and Keyboard
shortcuts, with colony-dependent actions disabled. Jukebox opens the native menu and
pumps native Radio updates against the host daemon. `BrowserAudioPatch` redirects
only `NativeJukebox` transport and metadata to `BrowserAudio`. Radio retains source,
mute, navigation, music/master volume, and daemon stop-ack ownership. One DOM audio
element decodes authored OST files staged under private runtime `assets/ost`;
no game audio enters the redistributable bundle. Song order is checked against the
authored SongDefs and files. Autoplay rejection waits for a pointer/key gesture;
frames never retry blocked playback. Ended advances the track, while mute, source
handoff, and workspace disposal retire decoding. Metadata follows actual browser
playback. Host radio/Spotify still require daemon capabilities and successful playback reports; a door/menu check
alone is not playback validation. Read-only door geometry in the
workspace snapshot supports DOM input checks without depending on fallback text
commands.

BrowserModKeys loads authored KeyBindings.xml through Verse's XML/reference loader
after Core, assigns the real mod content pack, and binds only ModDefOf key fields.
BrowserKeysPatch admits Entry in the actual shortcut prefix's state guard; its
focus/modal/binding-capture policy remains intact. Dispatch this prefix before
native deferred window input, and do not replay a consumed event into that window.
The known desktop Steam warning is dismissed at workspace initialization because
otherwise it silently blocks shortcuts after the fullscreen workspace closes.

BrowserGui implements installed UnityStubs IGuiBackend; gui.js presents Canvas2D
commands. Group/scroll origins, mouse translation, matrix restoration, and transformed
hit clips must agree. Font size zero means the imported font size. Metrics are
approximate for unsupported fonts and editing modes; overlap routing and
material/primitive drawing remain incomplete.
Drawing colors are clamped and rounded to bytes before transport, matching native
ImGui vertex colors for fills, images and glyphs. This alone does not establish
compositing parity: measured sidebar backgrounds still differ by one RGB level.
The opt-in `floatCanvas` workspace parameter requests float16 Canvas for the screen
and render targets, failing explicitly if unsupported. Real workspace readback matches
the native panel blend, but does not establish whole-workspace parity. Under the same
3072x1664, scale-2, two-core diagnostic configuration, one idle/output benchmark pair
increased median draw intervals from 109/205 ms to 185/294 ms. It remains an experiment,
not a default. Benchmark context attributes distinguish the effective mode from the URL.
Float16 direct PNG export can differ from RGBA8 readback; canvas-reference capture uses
explicit byte readback and writes its context/capture metadata beside the PNG.
The reference comparison reports dominant colors alongside pixel errors so broad
flat-color differences are distinguishable from layout and text mismatches.

`BrowserGui.Commands` owns typed drawing payloads and their generated reference writers.
`BrowserCommandWriter` writes production packets directly into a reusable UTF-8
buffer with bounded retained capacity, preserving property order, escaping and float formatting. New payloads
must be added to both writers; the game-free command check compares exact packets
with the reference serializer and checks recovery after failed and oversized writes.
The CommandTests `benchmark` recipe compares both paths on a synthetic output-shaped
packet with packed-quad caching enabled and disabled; host timing does not establish
WASM or end-to-end workspace speed. Opt-in `profile`
workspace URLs retain bounded stage timings. `inputQueueMs` measures the oldest
queued DOM event through the start of managed dispatch, including the device update.
Inspect frames with `inputEvents > 0`; empty frames report zero. This client-only
interval is separate from command/echo round trips and daemon capture timing.
Typing benchmarks also observe the first WebSocket keys send and delivery of the
matching acknowledgement screen for invocation-owned fixtures. These timestamps
share the browser clock. Message delivery is a JavaScript callback, not network
arrival, and delivery-to-observation includes managed continuation, hub pumping,
rendering and the benchmark's polling delay. Observers retain timings only and
restore the socket send method and message listeners after each phase.
The serialization diagnostic alternates
all writers against one identical live packet; it measures transport only and must
not be reported as an end-to-end workspace benchmark.
Adjacent image quads sharing texture and tint form a flat numeric batch. Any
intervening state/resource command ends it; Canvas preserves individual draw order
inside each batch. The GUI probe compares clipped, transformed and alpha-overlapping
batch pixels with individual draws. The host benchmark owns disposable Python
fixtures, guarded DOM input, per-phase measurements and cleanup; it restores the
original session without sending it input. Its switching phase invokes native Open
and measures attachment, so it does not replace sidebar-control verification. The
`typing` phase sends whole commands; `keystrokes` separately sends one ASCII key
at a time to an invocation-owned PTY in noncanonical mode and verifies its byte
reply without Enter. Each reply in `keystrokes` ends with a newline, stressing
full-viewport scrolling. The separate `inlinekeystrokes` phase overwrites one line
with carriage returns and fixed-width, unique byte markers. Its raw-mode setup and
explicit Enter/final newline happen outside input timing and profiled frames.
Both single-key phases reset the same fixed screen before running, retain wire
timing, and measure native receive-buffer observation, including 20 ms predicate
polling, rather than physical display latency. The fixture restores prior terminal
settings on completion or read failure. The inline phase complements the scrolling
stress workload; it does not replace the broader workload and correctness gates.
Large batches use packed little-endian double tables and ordered indices; the transport preserves the numbers
the original float JSON parsed into JavaScript, rather than substituting raw float32
values. Canvas decodes before playback and profiling. The focused command tests own
numeric equivalence and ordered Canvas calls; the opt-in serialization diagnostic
also reports the previous numeric batch writer against the same live packet.
Canvas retains decoded packed geometry under independent entry and byte bounds,
keyed by the complete encoded payload. This shares immutable coordinates across
changing frames without retaining textures or suppressing resource commands.
`gui-pixels.js` decodes immutable RGBA uploads using native base64 support when
available, sharing the returned buffer with ImageData's clamped byte view. Older
runtimes use an indexed byte-copy loop. Canvas retains conversion and tint ownership.
`browser-host-pixel-upload-check` compares decoded bytes and real Canvas pixels
against the callback decoder, including transparent texels and production drawing.
It times fresh Canvas allocation, decoding, ImageData and putImageData; readback,
GPU completion and whole-workspace latency are excluded. Its authored sources stream
to a disposable host directory so isolated worker paths need not exist on the host.
The Node-only `browser-bench-quad-decode` recipe measures repeated decoding;
it does not measure browser rendering or whole-workspace latency.
`BrowserPacketCache` retains one bounded,
deep-copied command snapshot and its JSON. It compares command order, all payload
fields and exact float bits; it never suppresses native drawing or resource playback.
The benchmark's final packet-cache argument toggles it within one runtime and restores
the prior setting after cleanup. Repeated idle packets benefit, but changing packets
allocate snapshot copies; evaluate whole-workspace latency and CPU separately from
serialization timing. The plain workspace URL enables it with idle yielding;
explicit probe URLs remain opt-in.
`BrowserCommandValueCache` independently reuses small serialized fill, matrix,
clip and target values across changed packets. It is enabled by default;
`Probe.WorkspaceCommandValueCache(bool)` returns the previous diagnostic setting
and clears retained values. Keys contain structural integer fields and exact float
bits, without retaining caller arrays. Entry and payload-byte bounds exclude
uploads and large geometry. The direct writer still owns JSON formatting and
command order; resource playback is unchanged. `CommandTests` checks exact bytes,
mutation, signed zero and failure recovery; its `benchmark` recipe measures
repeated and moving values on desktop. Miss-heavy workloads can cost more.
The browser benchmark accepts `command` comparison mode for same-fixture
off/on/on/off phases; output and scrolling comparisons cover changed frames
independently of the whole-packet idle cache.
`BrowserQuadCache` retains packed coordinates under entry/value bounds and verifies
the original float bits after hash lookup, including signed zero. Image identity,
tint and draw state remain outside the cache. The workload recipe accepts an on/off
cache argument and restores the prior setting after cleanup, allowing comparisons
in the same loaded runtime without changing geometry or browser configuration.
Workload CPU reports include the diagnostic browser's process tree, separating
renderer and GPU processes. Renderer task timing alone excludes SwiftShader's
multicore software rendering and is not total browser CPU. Percentages use one
logical core as 100%; RSS is summed and may double-count shared pages. Process
churn marks interval accounting incomplete rather than implying zero cost.
Reports record the effective cgroup CPU quota, including tighter ancestor limits;
compare runs under the same quota as well as the same browser backend. An unlimited
quota is distinct from unavailable cgroup accounting.
Bounded host probes disable the diagnostic browser's desktop session bus so Chromium
cannot move itself into an uncapped application scope. They verify the effective
quota before navigating to the workspace and fail if the requested bound is absent.
The Firefox workload runner uses direct geckodriver HTTP and the same fixture script.
`browser-host-firefox-bitmap-check` runs a game-free, headless Canvas/ImageBitmap
glyph playback comparison with exact pixel checks, alternating source order and
separate submission/readback timing under the same two-core bound.
The Firefox workload recipe's optional `canvas_profile` argument installs aggregate
Canvas method and frame-reuse preparation timing after startup. It distinguishes
screen/target calls and coarse source/destination image sizes. Expensive small
sources retain bounded dimension, uniform-color and draw-opacity summaries;
nonuniform image contents and commands are not recorded. This diagnostic includes timer overhead;
use an uninstrumented comparison to establish a workload speedup.
Its injected diagnostics execute in the document realm: WebDriver's isolated module
map would otherwise create a second GUI image bank. It owns its browser/process
group and temporary sessions; the recovery recipe checks host/temporary metadata
before deleting reported fixtures. Headless and visible Firefox can use different
Canvas backends; reports include allowlisted about:support graphics rows.
Validation defaults to Chrome's backend selection. Set `PROBE_RENDERING=swiftshader`
to reproduce the software-rendering configuration explicitly. Reports include actual
GPU devices and feature status; `auto` does not establish hardware acceleration.
`PROBE_RENDERING=angle-gl` explicitly selects ANGLE's GL backend without overriding
Chrome's driver blocklist. This can enable hardware Canvas in headless Chromium
where `auto` selects SwiftShader. It does not guarantee hardware on another host;
check the reported device and Canvas/compositing feature status for every run.
The host rendering check, bounded workspace launcher, and installed-runtime check
accept a final rendering-mode argument and forward it through systemd explicitly.
Do not compare performance across different rendering backends as a code speedup.
`browser-host-rendering-check` verifies reporting on a blank page under a one-core
CPU quota and closes the browser; it does not exercise the game workspace.
The scrolling workload records native line/pixel offsets and history request state
after each wheel event and after settling. Compare this trace as well as timing:
event counts alone do not establish equal rendered movement when frames coalesce
input. Snapshot reads are diagnostic observations, not part of normal rendering.

`reuseCanvas` skips Canvas replay only for byte-identical
screen packets at unchanged dimensions and render revision. Resource uploads,
target writes/releases, unknown commands, context loss and external rendering
invalidate reuse; animated backgrounds disable it. Native update/input and clipboard
pumping still run every frame. `browser-host-canvas-reuse-check` compares real
Chrome pixels for transformed alpha images, changed content, external uploads and
resizing under a one-core quota. Idle yielding enables this replay cache too.

`idleYield` also enables Canvas reuse.
After three unchanged frames it inserts a cancellable 16 ms idle wait. Input keeps
continuous scheduling for 500 ms; socket publications and HTTP completion wake the
loop without reentering native updates. Changed frames and animated backgrounds
remain continuous. This is a latency/CPU tradeoff, not proof that timed native
animations are equivalent. Benchmarks report actual workspace draw intervals
separately from browser animation-callback intervals so idle waits cannot appear
as faster native drawing. The plain workspace URL defaults to `idleYield` and
`packetCache`, following same-browser idle, output, typing, scrolling and switching
measurements. `/?idleYield=off&packetCache=off` restores continuous scheduling and
serialization for comparison. Explicit probe URLs opt in by including those flags.
This does not establish complete animation equivalence or visual parity.

Render textures retain Canvas backing across frames for the actual mod terminal
cache. Browser capability reporting disables GPU CopyTexture so that cache selects
its full-screen repaint branch. Image uploads follow native identity and
Apply/LoadImage revisions; version-object identity prevents recycled texture IDs
from inheriting another texture's upload record. NativeTextTests cover mutation,
backing/dimension replacement, discarded uploads, ID reuse and disposal.
Subsequent commands reference uploaded images rather
than copying an atlas per glyph. Discarded input/layout uploads must also discard
their managed cache entries. Weak ownership and release commands retire native
resources. Pixel conversion reverses Unity's bottom-up rows using bulk copies.
Every frame's commands must reach Canvas, including initial uploads later frames use.
`gui.js` owns an off-by-default selective target replay experiment through
`configureGuiTargetDamage`. Its bounded snapshots certify opaque resets and exact
ordered immutable-image row groups under positive axis-aligned transforms. Only
integer interiors qualify; fractional fringe and changed or unsupported regions
retain replay, including reset and intersecting draws in their original order.
Resource, target, display, context and rendering-setting changes invalidate reuse.
Native terminal cache policy and packet production are unchanged. Node planner
tests protect the certificate and invalidation; they do not establish real Canvas
pixel parity or a workload speedup.
Immutable uploaded images retain one Canvas readback for tint variants, preserving
Canvas alpha quantization. Tint variants retain at most 32 colors per image, also bounded by the existing
32 MiB per-image budget (with one variant allowed for an oversized source), and
are discarded with their source upload. `configureGuiTintCache` exposes the
16-entry reference for diagnostics; `configureGuiTintProfile` enables counters
for immutable-image misses and mutable-target tint work without retaining pixels
or command contents. The benchmark's `tint` mode runs four identical phases in
off/on/on/off order and records those counters. Mutable render targets must still read their current pixels.
Constant immutable uploads and tint variants of at most 256 texels can use a
synchronous 1x1 Canvas backing for drawing. The original Canvas still owns tint
readback and cache sizing. Weak source keys bound the equivalent backing to its
upload/tint lifetime; mutable targets never qualify. Normalized UVs, alpha,
clipping and transforms still flow through `drawImage`. This path defaults on;
`configureGuiUniformImages(false)` selects the original sources for diagnostics.
The Firefox benchmark's `solid` mode compares it off/on/on/off.
`browser-host-firefox-solid-check` and `browser-host-solid-check` verify real
Firefox/Chromium pixels, including alpha quantization, fractional geometry,
signed/out-of-bounds crops, replacement and nonuniform fallback, without WASM.
Screen-space clips use a direct physical-space `clipScreen` path by default:
rectangle corners retain signed/fractional coordinates directly, while `group` and `clip` still map corners through the active transform.
`configureGuiScreenClipFastPath(false)` selects the transformed reference for diagnostics.
Both paths retain the same physical clip stack for render-target inheritance. The benchmark's
`clip` comparison uses off/on/on/off and restores the prior mode;
`browser-host-screen-clip-check` compares real Canvas pixels for nested transformed
clips, empty/negative/fractional rectangles, pops and target transitions without
starting the game. This correctness check makes no workspace performance claim.
`tint.test.mjs` protects byte rounding; `tint-check.js` compares the old and new pixel
loops in Chrome, excluding Canvas upload and complete workspace repaint costs.

BrowserGuiInput/gui-input.js dispatch ordered DOM events through installed controls.
`WorkspaceDiscardedDrawing` is a disabled diagnostic switch that suppresses raster
command preparation during discarded input/layout passes. Native event dispatch,
measurement, coordinates and hit testing still execute, and exceptions restore the
prior drawing state. `browser-host-discarded-drawing-check` compares installed-label
repaint commands with the switch off/on; the benchmark's final argument controls it
and restores the previous value after fixture cleanup. It is not a demonstrated
material speedup and remains off.
The workspace flushes terminal input once after the complete ordered DOM batch,
before layout, repaint and command serialization, matching native keyboard dispatch.
`WorkspaceInputFlushBeforeRepaint(false)` restores the post-serialization boundary
for diagnostic A/B measurements and returns the previous setting; restore it after
the measurement. Both paths use the native panel flush and transport guards.
UTF-16 units stay in one batch until the actual terminal input flush. Modifier
shortcuts use physical keys; ordinary text does not duplicate physical keydown.
AltGraph printable keys enter the text path with synthetic Ctrl/Alt modifiers
removed, preserving layout-generated characters without invoking bindings.
Ordinary Ctrl+Alt chords retain their shortcut behavior.
The workload recipe's `early_input` argument compares the native input flush
before repaint with the previous after-serialization boundary in one browser;
cleanup restores the prior mode. Reported typing latency still measures DOM input
to native receive-buffer echo, not physical display latency.
The optional `comparison` mode (`input`, `glyph` or `cell`) requires four identical
phases and toggles off/on/on/off while retaining the same fixture sessions.
Before each measured phase it repaints the same fixture screen and waits for its
unique acknowledgment outside the timing/CPU window, avoiding content drift.
Phase-specific echo/completion markers prevent earlier screen contents from
satisfying a later run; cleanup restores all diagnostic switches.
Installed Waylith stores Event.alt and Event.shift separately from Event.modifiers;
populate both representations before native shortcut dispatch.
Printable events retain their physical key on the same text-bearing event so native
binding capture can identify a letter without generating a second character.
Focus loss clears native button capture as well as GUI focus. BrowserClipboard
replaces desktop clipboard operations while retaining native paste callbacks and
their pane/run guards. DOM paste supplies text once; both GUI and physical input
adapters must allow the browser's Ctrl+V default action. Copy/context-menu dispatch
flushes during user activation. Async permission failure clears the native fallback
buffer; PRIMARY remains local. PNG paste captures the event's file before async
reading and completes the original native request; host terminals remain text-only.
Synchronous clipboard frames pump physical input before native dispatch so mouse
selection capture sees the current buttons. Trusted Chrome checks exercise Unicode
text, native selection copy and PNG delivery into a disposable Codex composer,
without submitting a prompt. Wait for composer initialization: Codex can treat
image paths pasted during startup as provisional text.
`text-input.js` owns one shared editable textarea per canvas. Canvas focus and
pointer presses hand keyboard focus to it; GUI and physical adapters treat that
handoff as internal, while outside focus loss cancels preedit and resets capture.
Refocus admits fresh typing without reviving a canceled composition's delayed end.
Window blur and tab hiding release shared IME ownership even if the textarea
remains the active element, so physical keys resume with GUI input on return.
Trusted typing uses DOM input commits with the originating physical binding identity;
composition preedit and IME-owned physical keys never enter native dispatch.
Composition commits enter one ordered UTF-16 batch, without a binding identity or
synthetic Ctrl/Alt modifiers. Canvas-targeted synthetic key events remain diagnostic
inputs. The proxy is positioned at the last pointer press, not the native caret;
accurate candidate-window placement and full OS IME parity remain unverified.
`browser-host-composition-check` drives Chromium preedit/update/commit and ordinary
key events into disposable terminals. An ASCII hex reply verifies the exact received
UTF-8 bytes independently of screen escape sequences and glyph coverage; cancellation
and composing Enter must not leak input. This does not exercise an OS candidate UI.
BrowserInput/input.js separately feed Waylith and Unity device
state. The end-to-end harness sends DOM input only into its newly created temporary
host shells, observes the actual SessionHub ScreenBuf, and removes only those
shells. Clipboard checks use a real Chromium Ctrl+V shortcut and verify native
selection against the browser clipboard. Existing coding-agent attachment is read-only; host credentials remain
outside diagnostic scripts and reports.

Workspace runs disable managed debugging by default so Mono's Jiterpreter can run.
`managedDebug` is an explicit diagnostic override. This is independent of Debug
versus Release build configuration.
Workload reports take read-only Jiterpreter snapshots before and after each phase,
outside its measurement window. They include runtime options, generated code size,
trace count and cumulative compilation time, so code limits and warmup can be
checked rather than inferred from frame time. The helper follows the pinned .NET
runtime's internal counters; unavailable diagnostics do not replace workload results.

## Separate engine/rendering probes

Core bootstrap stages installed Core data/shaders. Serial jobs preserve native
job-handle semantics; explicit platform boundaries replace unavailable desktop
logging/audio/services. Returning frames do not prove successful loading: inspect
definition counts and engine errors because UnityWorld catches component exceptions.
Core-only admission rejects mod assemblies and does not implement custom CLR field
injection/profiling.

GameSession leaves generation with installed Root_Play; GameStorage leaves
serialization with the game. Save experiments use one local exported save, not
host save directories or persistent browser storage. Restore checks retain world
and pawn identity before simulation validation. Process-lifetime state requires a
fresh WASM runtime after shutdown.

BrowserDraws/BrowserScene and terrain/object adapters consume actual meshes,
transforms, and material snapshots. Keep uniform/material resolution with the
installed resolver while its property arena is valid. Unity matrix columns need
explicit conversion; projection matrices already encode Vulkan Y/depth conventions.
Configure viewport/UI scale before map updates because culling consumes them even
without native OnGUI.

Core stages retain CPU pixels and reject unreadable GPU atlases, preserving native
non-atlas drawing. Pawn pre-render selects its disableCache branch before preparing
the render tree; rejecting lookup later loses the pawn. These probes establish
subsets, not a complete renderer or playable game.
