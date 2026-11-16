# Browser product ownership

`slopweb/` owns the installed browser product. Its [README](../slopweb/README.md)
owns usage. Private experiments, research reports, browser automation, and retained
diagnostic runtimes belong in ignored `priv/`. Public builds, tests, packaging,
and CI must work without private files.

`host/src/launcher/` owns preparation, compilation caching and launch; its CLI uses
`slopd/src/shared/application_paths.rs` for shared roots and endpoint/profile paths.
`host/src/server/` owns asset delivery and the authenticated daemon bridge.
`host/src/fonts/` owns native font discovery, coverage and immutable font files.
`runtime/` owns Entry-only WASM execution, browser assets, adapters and native
preparation helpers; `offline/` patches private copies of assemblies.
Browser scale adaptation removes desktop DPI inference from private game copies;
native preference accessors retain ownership of the selected scale.
The offline Steam boundary disables startup and removes Steamworks native callback
markers from the private copy so WASM AOT does not generate desktop callback wrappers.
`tools/release/slopweb.py` stages reviewed redistributable inputs. Installed game
assemblies, derived assets, fonts, and credentials never enter release payloads.
Distribution containers reuse the managed/WASM bundle and compile the Rust host
for their own architecture and libc. `SLOPWEB_HOST_BINARY` selects that staged
replacement; it also regenerates the bundle identity. Packagers preserve its
executable mode under `usr/share/slopweb` and discover its native dependencies.

`just/web.just` owns product builds and checks. Root format, lint, test, clean,
and dependency recipes include slopweb. Python tests run once through the shared
pytest inventory; component tests remain independently runnable. All browser project
locks participate in restore/update, and managed projects inherit the shared analyzer
policy. The installed application has no Python or Node runtime dependency; Python owns build scripts and Node runs JavaScript tests.
Cleanup removes project bin/obj, the redistributable bundle, and generated font-module
outputs; compiler caches and retained private runtimes remain separate.

The `Probe` assembly/type name is retained as the managed export and offline bootstrap
ABI. Its authored compiler allowlist contains only workspace startup, transport/input,
and presentation exports. Colony creation/saves, compatibility experiments, synthetic
controls, and benchmark fixtures belong in private probe projects. Package staging copies
only the framework subtree from build output and current authored web assets, so stale
experimental pages cannot leak from incremental build directories.
 `tests/runtime/` owns managed contracts, `tests/browser/` owns browser JavaScript
contracts exercised in Node, and Rust tests sit beside their owners under `host/src/`.
Packaging assertions remain under `tools/release/`. `ci-web` runs game-free checks;
native font fidelity checks are explicit opt-ins.

The browser workspace stays in Entry without creating a game, world, or map.
Scene status reports only Entry state and viewport dimensions, without traversing
simulation objects. Detailed workspace snapshots are opt-in with profiling;
observation errors remain diagnostic state and cannot stop rendering. Private
probe exports own sidebar/catalog inspection and terminal assertion helpers.
Underlying chrome binds native accessors and geometry metadata once, while
reading current geometry on each draw.
Startup validates managed status reports as well as exceptions; failed or incomplete
Entry initialization releases the engine and ends loading with an error.
Game assembly downloads use a four-file window, including completed buffers;
CLR loading and per-file failure reporting retain manifest order.
All startup assembly loads share a filename-bearing boundary that AOT preparation
rewrites to published identities; byte loading must not bypass it.
Assembly loading, archive transfer, font installation and startup stages yield to
browser painting between work batches; individual synchronous managed calls still
own their uninterrupted cost.
Browser middle-click paste is owned by the DOM paste event carrying desktop PRIMARY;
both browser input adapters suppress the corresponding managed button gesture to
avoid also pasting the adapter's saved local selection.
PRIMARY copy publishes the native selection through the editable DOM proxy's text
selection; it does not write CLIPBOARD through the browser Clipboard API.
The Waylith input-frame boundary explicitly clears retained events; reflection
does not supply native optional parameter defaults.
GUI focus resets follow accepted text and key events; only unfinished composition
is canceled. Repeated blur/visibility notifications share the pending reset.
The mod retains sidebar, terminal, native text layout, session catalog, and
configuration ownership. Adapters substitute platform services and rendering;
the daemon retains processes, tmux, sandboxes, and credentials. See the
[client boundary](mod-client.md) and [wire contract](protocol-wire.md).
Host appearance import and browser persistence share a tested presentation
allowlist. Offline UI hooks invoke the mod’s action-caption backdrop and translated settings-row
filters; the browser does not duplicate their theme or visibility policy. Loading
publishes the original game background while frames decode. Background animation
and quote scrolling start independently as their own presentation assets arrive;
failed optional assets must not hold the other animation.
Workspace startup takes ownership of pending background downloads as well as ready
frames; loading-screen teardown must not cancel a transferred load.
Browser backdrop rendering consumes the native dimming preference;
profile files stay read-only and browser changes remain local to its origin.
The same preference boundary retains the jukebox pause flag. Browser tab titles
use the mod's window-title policy; packaging reuses the documentation favicon.
Viewport density supplies the initial UI scale. Each GUI pass synchronizes glyph
density with the native scale preference before layout; backing-canvas resizes
preserve an explicitly changed scale. Font-bank revisions retire old glyphs and
invalidate text layout and row caches.

Static assets use private browser caching with revalidation on every reuse;
the static-file owner supplies validators so unchanged reloads avoid body transfers.
Its `X-Slop-Decoded-Length` header carries the original file size through proxy
compression; archive startup validates decoded bytes against that size.
Bridge responses and font catalogs remain uncached; content-addressed font files
retain their immutable caching policy.

The server binds IPv4 loopback and validates Host separately from browser
provenance. Its same-origin HTTP and WebSocket bridges inject host credentials;
credentials never enter page assets or URLs. Named endpoints take precedence over
paired URL/token environment overrides, then `SLOPD_ENDPOINT`, then local.
The host reads descriptor credentials for each operation and WebSocket reconnect;
failed reads fail closed, and mutations are never replayed after authentication failure. Remote access requires an
authenticated HTTPS proxy and explicit `--public-origin`; forwarded headers never
select trusted origins. HTTP, font and WebSocket requests share that origin boundary.
The managed client owns HTTP operation deadlines; browser cancellation aborts bridge
I/O without replaying mutations. Browser transport keeps native request admission, deadlines,
completion delivery, connection generations, and bounded queues. Overload closes
the socket rather than blocking the UI thread.
Discarded input/layout passes execute native controls with raster-command
preparation suppressed; only repaint publishes drawing. Stable presentation backs
off up to 100 ms, bounded by native cursor animation and terminal blink/resize/scroll
deadlines. Input and published transport activity cancel pending idle waits.
Append-only font atlases retain CPU pixels until drain, coalescing publication and
encoding once per atlas per packet. General mutable textures still snapshot at each
mutation to preserve draw order. Failed drains discard resource claims so retries
must upload their definitions again.
Browser GUI packets retain one bounded decoded frame; command consumers leave it
immutable, while Canvas reuse still checks dimensions, resource revisions, and
resource-changing commands. Startup consumes every packet before requesting a font
redraw: image uploads and retained-target changes cannot be dropped when replacing
a frame, even while the loading screen covers the workspace.
Glyph persistence uses a bounded asynchronous write
queue so storage latency does not delay glyph replies or worker font installation.

Host preparation composes immutable runtimes from independently cached patched
assemblies, archives, fonts, and backgrounds. `host/src/launcher/aot.rs` owns compiler inputs and
toolchain identity. Publication locks span composition; workspace locks span input
synchronization, compilation, and output capture, including orphaned compiler
processes. Compiler output writes directly to its durable log and is mirrored live
to the host terminal; dynamic font diagnostics also stream to the host, while helper
stdout remains a machine-readable cache path. Only completed publications are served. Interpreted execution is the default; `--aot`, `--aot-sdk`, or
`SLOPWEB_AOT_SDK` select mixed AOT, while `--interpreted` overrides the environment.
Packages depend on the .NET SDK; AOT additionally needs its matching wasm-tools workload.
Installed launchers validate AOT SDKs before game preparation; the checkout's
launcher `--ensure-workload` switch provisions workloads only for AOT.
`tools/web_build.py` coordinates timed source builds. Independent managed helpers build
concurrently (up to six; `SLOPWEB_BUILD_JOBS` overrides the limit), with a completion
barrier before staging. Adding shared project outputs requires revisiting that graph.
The checkout AOT recipe defaults to eight compiler workers; its `cores` argument
sets the .NET, Binaryen, and Emscripten limits together.
Content archives use canonical ZIP metadata so identical payloads retain runtime identity.
Launch prints preparation results before its URL.

Host font preparation writes outlines and native loader settings, never glyph images.
The Rust service persists content-addressed standalone faces, cmap coverage and
Fontconfig ordering independently of application builds. Source and configuration
identities invalidate discovery; unchanged faces reuse coverage. A .NET helper reads
native loader flags only when its game/helper identity changes, outside glyph requests.
The browser fetches the index for each selected family, chooses coverage locally,
and downloads only required immutable files. Fallback chains retain Fontconfig's
monochrome filtering; catalog-only families must not be appended to them.
Verified browser file storage is bounded
and survives reloads. Generic preloads and actual initial-frame coverage settle before
the workspace is shown; neither uses an authored glyph list.
Preloads overlap at most two families per warm pass; shared fallback files join one
installation through worker loading and managed publication, including failure delivery.
`BrowserFontBank.Fallbacks` owns pending requests and maps resolved fonts into the
primary atlas at its baseline; replies never expose host paths or glyph pixels.
`LocalFonts/` supplies FreeType WASM; browser adapters own rasterization and atlas
lifetime, while native text layout retains wrapping and caret policy. Non-ASCII
glyphs rasterize in a worker and use bounded origin-local persistent storage keyed
by font content, scalar, size, density, and loader flags. Pending glyphs remain
retryable until completion invalidates native drawing. Startup drains glyph replies
and publishes one invalidation before its next draw; live replies remain coalesced.
Font bytes are installed in both rasterizers before managed publication; the main thread retains synchronous
metrics and ASCII rendering. Bump the glyph storage namespace when changing
FreeType or its rasterization contract. Selection
changes publish after loading and reject stale responses. Both execution modes use
the same font pipeline; AOT only changes managed execution. Private font bytes and
pixels remain in local caches. Reload after changing the installed host collection.

Unity URL actions route through the browser window adapter, with native terminal hit
testing retained. Pointer dispatch opens HTTP(S)/mailto links synchronously in a new
tab without an opener; executable and local-file schemes are rejected.

Browser cursor presentation uses one DOM canvas overlay at every viewport position,
with native animation pixels and device-pixel hotspots. Fullscreen uses F11; optional
Keyboard Lock keeps Escape available to native input when supported and permitted.
