# Browser product ownership

`slopweb/` owns the installed browser product. Its [README](../slopweb/README.md)
owns usage. Private experiments, research reports, browser automation, and retained
diagnostic runtimes belong in ignored `priv/`. Public builds, tests, packaging,
and CI must work without private files.

The root Python modules own host preparation, compilation caching, and launch.
`runtime/` owns Entry-only WASM execution, browser assets, adapters, and native preparation
helpers; `offline/` patches private copies of assemblies. `server/` owns asset
delivery, font delivery, and the host-authenticated daemon bridge.
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

The server binds IPv4 loopback and validates Host separately from browser
provenance. Its same-origin HTTP and WebSocket bridges inject host credentials;
credentials never enter page assets or URLs. Remote access requires an
authenticated proxy. Browser transport keeps native request admission, deadlines,
completion delivery, connection generations, and bounded queues. Overload closes
the socket rather than blocking the UI thread.

Host preparation composes immutable runtimes from independently cached patched
assemblies, archives, fonts, and backgrounds. `aot.py` owns compiler inputs and
toolchain identity. Publication locks span composition; workspace locks span input
synchronization, compilation, and output capture, including orphaned compiler
processes. Only completed publications are served. Installed launchers inspect
SDKs; the checkout's `just_launch.py` owns explicit workload provisioning.

Host font preparation uses the installed mod catalog and native resolver for
font selection and metrics. `LocalFonts/` supplies the independent FreeType WASM
module; browser adapters own atlas lifetime and rasterization, while native mod
text layout retains wrapping and caret policy. Private font bytes and derived
pixels remain in local caches. Font changes commit after successful loading and
reject stale responses; replacing installed font files requires a rebuild.
