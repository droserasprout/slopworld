# Browser product ownership

`slopweb/` owns the installed browser product. Its [README](../slopweb/README.md)
owns usage. Private experiments, research reports, browser automation, and retained
diagnostic runtimes belong in ignored `priv/`. Public builds, tests, packaging,
and CI must work without private files.

The root Python modules own host preparation, compilation caching, and launch.
`application_paths.py` mirrors the native shared path resolver for application
roots, endpoint profiles, and saved game discovery. Keep those contracts aligned
with `slopd/src/shared/application_paths.rs` and the native launcher's game resolver;
the installed Python launcher must remain independent of checkout tooling.
`runtime/` owns Entry-only WASM execution, browser assets, adapters, and native preparation
helpers; `offline/` patches private copies of assemblies. `server/` owns asset
delivery, font delivery, and the host-authenticated daemon bridge.
The offline Steam boundary disables startup and removes Steamworks native callback
markers from the private copy so WASM AOT does not generate desktop callback wrappers.
`tools/release/slopweb.py` stages reviewed redistributable inputs. Installed game
assemblies, derived assets, fonts, and credentials never enter release payloads.

`just/web.just` owns product builds and checks. Root format, lint, test, clean,
and dependency recipes include slopweb. Python tests run once through the shared
pytest inventory; component tests remain independently runnable. All browser project
locks participate in restore/update, and managed projects inherit the shared analyzer
policy. Installed Python tools receive strict type checking independently of packaging.
Cleanup removes project bin/obj, the redistributable bundle, and generated font-module
outputs; compiler caches and retained private runtimes remain separate.

The `Probe` assembly/type name is retained as the managed export and offline bootstrap
ABI. Its authored compiler allowlist contains only workspace startup, transport/input,
and presentation exports. Colony creation/saves, compatibility experiments, synthetic
controls, and benchmark fixtures belong in private probe projects. Package staging copies
only the framework subtree from build output and current authored web assets, so stale
experimental pages cannot leak from incremental build directories.
 `tests/runtime/` owns managed
contracts, `tests/browser/` owns browser JavaScript contracts exercised in Node,
`tests/server/` owns host server contracts, and `tests/launcher/` owns preparation
and cache tests. Packaging assertions remain under `tools/release/`. `ci-web`
runs game-free checks; native font fidelity checks are explicit opt-ins.

The browser workspace stays in Entry without creating a game, world, or map.
The mod retains sidebar, terminal, native text layout, session catalog, and
configuration ownership. Adapters substitute platform services and rendering;
the daemon retains processes, tmux, sandboxes, and credentials. See the
[client boundary](mod-client.md) and [wire contract](protocol-wire.md).
Host appearance import and browser persistence share a tested presentation
allowlist. Browser backdrop rendering consumes the native dimming preference;
profile files stay read-only and browser changes remain local to its origin.

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

Host preparation composes immutable runtimes from independently cached patched
assemblies, archives, fonts, and backgrounds. `aot.py` owns compiler inputs and
toolchain identity. Publication locks span composition; workspace locks span input
synchronization, compilation, and output capture, including orphaned compiler
processes. Compiler output writes directly to its durable log and is mirrored live
to the host terminal; dynamic font diagnostics also stream to the host, while helper
stdout remains a machine-readable cache path. Only completed publications are served. Interpreted execution is the default; `--aot`, `--aot-sdk`, or
`SLOPWEB_AOT_SDK` select mixed AOT, while `--interpreted` overrides the environment.
Packages depend on the .NET SDK; AOT additionally needs its matching wasm-tools workload.
Installed launchers validate AOT SDKs before game preparation; the checkout's
`just_launch.py` provisions workloads only for AOT. `build_tools.py` coordinates timed source
builds; `timings.py` reports wall times, and launch prints preparation results before its URL.

Host font preparation uses the installed mod catalog and native resolver for
font selection and metrics. `LocalFonts/` supplies the independent FreeType WASM
module; browser adapters own atlas lifetime and rasterization, while native mod
text layout retains wrapping and caret policy. Private font bytes and derived
pixels remain in local caches. Font changes commit after successful loading and
reject stale responses; replacing installed font files requires a rebuild.
