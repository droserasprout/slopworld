# slopweb

Local browser Eco workspace using the installed Waylith/RimWorld 1.7 game and
SlopWorld mod in WebAssembly. No colony is created or loaded. Agent processes,
tmux, sandboxes, and daemon credentials stay on the host.

## Source layout

The root Python modules own local preparation, compilation caching, and launch.
`runtime/` owns managed WASM execution, adapters, browser assets, and native helpers.
`offline/` owns assembly preparation hooks. `server/` owns the Node asset server,
host-authenticated daemon bridge, and font delivery.
`tests/runtime/` holds managed test projects; `tests/browser/` holds game-free
JavaScript contracts and opt-in native font checks; `tests/server/` holds Node server tests; `tests/launcher/` holds Python launcher, AOT,
and font tests. Package and archive assertions stay in `tools/release/`.
All test recipes live in `just/web.just`: `just web-test` runs the game-free
managed, Node, and Python suites; `just web-test-runtime` runs the managed
and related Node contracts. Native installation and font checks have explicit `web-test-native-*`,
`web-test-keybindings`, and `web-test-local-fonts` recipes.
`web-check-native-build` compiles native fidelity checks without executing them. Formatting and linting use
`just web-format` and `just web-lint`, also included in root maintenance
commands. `just web-clean` removes build output while preserving compiler caches
and installed/private runtimes. `just web-restore` restores reviewed project locks;
`just deps-update-web` updates those graphs.
`out/` is the generated redistributable bundle, assembled by
`tools/release/slopweb.py`; edit its source owners instead.

Private experiments, research reports, and browser automation belong in ignored
`priv/`. Product builds, tests, and CI do not depend on that directory. Installed
runtime caches remain outside the source tree.

## Install and run

From a checkout with the .NET 10 SDK, Node.js 22+, Emscripten 7.0.0, Python/uv, Just, and a local
Waylith/RimWorld installation:

```sh
just web-local-fonts-sdk # Install the pinned compiler locally if needed.
just web-install
slopweb
# Open http://127.0.0.1:8787/
```

Source installation defaults to `~/.local`; ensure `~/.local/bin` is on PATH.
Pass a prefix to `just web-install PREFIX` or `just web-uninstall PREFIX`.
Uninstall preserves private runtime caches and daemon configuration.

Installed packages require the .NET 10 SDK, Node.js 22+, Python 3.11+, and Fontconfig;
Just, uv, and a checkout are unnecessary. Interpreted mode is the default and needs no
wasm-tools workload. For optional mixed AOT, install the matching `wasm-tools` workload with `dotnet workload install wasm-tools`
for the selected SDK (system SDKs may require sudo). Package managers install the SDK;
.NET owns its workloads. The launcher validates AOT tools when AOT is selected, before preparing game data. Start slopd first. The launcher
reads its host-side local endpoint descriptor, respecting `SLOPD_CONFIG_ROOT`
and `SLOPD_ENDPOINT`. `SLOPD_URL` and `SLOPD_TOKEN` override that descriptor.
Use a root token for session creation and configuration. `--endpoint NAME` selects a saved endpoint ahead of environment overrides.
Explicit connections require both `--url URL` and `--token-file PATH`.
Descriptor and token-file changes apply to subsequent requests and WebSocket reconnects. Tokens are never placed
in browser assets or URLs.

Game discovery follows the native launcher: `--game PATH`, `SLOPWORLD_GAME`,
the saved `<CONFIG>/game.toml`, then usual installation locations. `--port` defaults
to 8787. First launch prepares local game files under `$XDG_CACHE_HOME/slopweb`
(default `~/.cache/slopweb`); subsequent launches reuse that private runtime.
`--prepare` prepares without serving; `--rebuild` refreshes after game data changes.
The server binds only to 127.0.0.1. For an authenticated HTTPS reverse proxy,
pass `--public-origin https://workspace.example.com`. The proxy must authenticate
all asset, HTTP, font, and WebSocket requests and forward them to the loopback server.
Preserve the browser Origin/Referer and use either the configured public Host or the
loopback upstream Host. Forwarded headers do not select trusted origins. Without this
option, only loopback Host values and their HTTP origins are accepted.
Stop it with Ctrl+C; agents continue running.
`just web-run` builds and stages current sources, then runs the launcher in the calling environment,
with its existing terminal and environment variables. Run it from the desktop host
when host game files, fonts, or daemon configuration are needed.
Build scripts print per-step wall times and a total. The launcher prints preparation
timings before the connection URL (also with `--prepare`); cache hits are reported as
reuse, and the game-preparation subtotal includes its component steps.
The printed root URL applies desktop defaults without redirecting. Preparation
imports only allowlisted appearance preferences from the selected endpoint's profile and prepares
its UI and terminal font metrics through the installed native rasterizer. Glyphs
are rasterized on demand from the text actually displayed. Font files and
atlases stay in the private cache; changed appearance preferences select a new
cache. Use `--rebuild` after changing installed font files.

Profile discovery respects `SLOPD_DATA` and `SLOPWORLD_PROFILE`; `--profile PATH`
selects an explicit profile directory. `--sidecar` selects the same sidecar endpoint
and profile paths as the native clients. Browser settings changes stay in browser
storage and do not overwrite that desktop profile. `slopweb paths NAME` exposes resolved
paths without preparing the game or connecting to the daemon. `slopweb --version`
reports build metadata, and `slopweb completions bash|zsh|fish` emits shell completions.

## Optional mixed AOT

Preparation and serving use the interpreter by default. Pass `--aot` to enable mixed
AOT with `dotnet` from PATH. Set `SLOPWEB_AOT_SDK` or pass `--aot-sdk PATH` to select a separate toolchain:

```sh
slopweb --aot-sdk /private/dotnet/dotnet --prepare
slopweb --aot-sdk /private/dotnet/dotnet
```

An explicit AOT request fails if its tools are missing. `--interpreted` selects the
interpreter even when `SLOPWEB_AOT_SDK` is set; it cannot be combined with `--aot-sdk`.

PATH selects an existing .NET 10 SDK directory or its `dotnet` executable with
matching `wasm-tools`, including the WebAssembly SDK and browser cross compiler
packs for that SDK's runtime patch. The launcher validates the toolchain and pins
the selected SDK and target runtime for publishing. The installed launcher never installs tools. When AOT is selected, checkout `just web-run`
and AOT preparation recipes automatically install
missing or mismatched `wasm-tools`, pinning the workload set to the selected SDK
version. Protected system SDKs use `sudo` and may prompt for a password; writable
private SDKs install directly.
Initial compilation requires substantial time, disk and memory; the launcher
prints the private cache and publish log paths. Compiler output streams live to the
terminal while remaining in `publish.log`; failures and retries retain earlier attempts. NuGet restore may download the
project's pinned package dependencies.

Compilation is reused for identical generated compiler inputs, final patched DLLs,
settings, and the selected SDK/WASM toolchain. Managed edits reuse a locked compiler
workspace and its incremental intermediates; compiler-policy or toolchain changes
select a separate workspace. Published runtimes remain immutable.
Game preparation caches patched assemblies, game archives, fonts, and backgrounds
separately. Browser assets and appearance changes reuse unaffected components and
compose a new runtime using the existing compiled framework. `--rebuild` refreshes
all preparation components, including installed font and native background-cache
changes; unchanged compiler inputs still reuse compiled AOT.
Failed or interrupted publishes resume in the same compiler workspace. Only complete
runtimes are served, and existing serving roots survive refreshes.

Compiler intermediates stay under `--cache/aot-workspaces/`; completed publications
and per-input logs stay in `aot-builds/`. Immutable serving roots live in
`aot-runtimes/`. Preparation components live in `prepared-components/`.

The host preparation recipe defaults to two compiler workers. Pass a positive integer
as its third argument to change .NET and Binaryen parallelism. Recipes inherit resource
limits from the caller; they do not create systemd services or impose CPU quotas:

```sh
just web-prepare-aot /private/dotnet/dotnet /private/slopweb-cache 4
```

The local font rasterizer also caches successful builds by source, compiler, flags,
and port identity, verifying all output files before reuse.

Mixed AOT retains application IL and reflection metadata, roots all application
assemblies, and keeps Waylith, Waylith.Injection, and
RimWorldWaylith.PrepatcherCompat interpreted. Its loader resolves published
assembly identities by name; the `skipLogger` alternate DLL option is disabled.

## Packages

`just web-build` builds without an installed game, using pinned RimRef
assemblies for compilation. These references never enter the package. RimWorld
is required only when the installed launcher prepares and runs the workspace.

```sh
just package-web arch
just package-web debian
just package-web fedora
# Equivalent: just package-product DISTRIBUTION slopweb
```

For an already-built bundle, `just web-package-staged DISTRIBUTION VERSION REVISION`
creates a local archive without rebuilding the private development runtime. Pass
a Git revision (or `HEAD`) for reproducible package timestamps. This does not
publish or install the archive. `just web-test` inspects the newest local Arch
archive when present, including its extracted launcher and private-asset exclusions.

Use the corresponding distribution's packaging tools. slopweb is independent of
the ordinary native package group. Packages include our browser tools, mod UI
assets, and redistributable dependencies. They exclude game assemblies, Core
data, shaders, saves, daemon credentials, and generated runtime caches. Those game
files are read from the user's own installation during local preparation.

## Interface

The sidebar selects agents; `+` creates sessions. Session-only agent rows share
native text presentation and the existing worker icon, without per-session portrait
preparation. Core and Jukebox doors open native menus without a colony; map actions
are unavailable. Jukebox OST plays in the browser with normal source, transport,
and volume controls. Browser autoplay restrictions may require a click or key press
before music starts. Radio/Spotify controls use the host daemon's
capabilities and audio output. Ctrl+V pastes browser
text, plus PNG images in ready Codex agents; selected terminal text copies with Ctrl+C or Ctrl+Shift+C. Without a selection, Ctrl+C still
interrupts the terminal process. Browser clipboard permissions apply to context
menu actions. Settings are available through the gear icon.

Web and game may share a terminal: the browser sends new pane sizes on local
geometry changes or reconnect, and does not continually override another client's
size. A shared tmux session still has one grid, so a wider client can leave content
outside a smaller pane's visible area.

This remains a prototype. An empty session catalog opens the native sidebar and
TopBar for session creation; startup also leaves these controls available when the
daemon is unavailable. An explicit session URL still requires that live session.
Text requests native glyphs, sizes, styles and display densities on demand;
startup prepares only font metrics. There is no fixed script/block coverage list.
Text can temporarily use approximate rendering while its atlas is prepared.
The installed launcher exports a matching native background
cache when available; generating missing frames on cold startup remains incomplete.
Text copy/paste and PNG attachment delivery have browser-to-agent checks. Wait for
the Codex composer to finish initializing before pasting an image; its provisional
startup input can retain image paths as text. Browser composition commits and
cancellation have Chromium checks; full OS IME behavior and precise candidate-window
positioning remain unverified.
Presentation settings persist in this browser origin when local storage is available;
`localPreferences=off` disables this for reference comparisons. Other profile fields
are not persisted. Authored SlopWorld keyboard bindings also persist in this origin,
including both binding slots and reset-to-default changes. Font family and size
changes load host font files on demand. Glyphs rasterize synchronously in local
FreeType WASM at the observed size, style and display density. Existing fonts
remain usable during preparation; a failed selection restores the previous settings
and offers retry. The private cache and retained atlas memory are bounded; reload
the page if the atlas budget is exhausted. Coverage depends on the selected font. The native text adapter still uses UTF-16
characters: missing font glyphs and supplementary text outside the existing
terminal emoji path use approximate rendering. This does not add a shaping engine.
Repeated page-network interruption and terminal recovery have browser checks;
daemon restart, run replacement, and the full agent launch/edit dialog have not
been exhaustively validated. See [ownership boundaries](../notes/slopweb.md) for the product architecture.

The standalone release installer also offers `slopweb` on x86-64 Linux:

```sh
python3 install.py --install slopweb
python3 install.py --remove slopweb
```

Installation requires a published release containing the matching `slopweb` package
and checksum. For locally built preview packages, use the distribution package manager.

Local glyph rasterization uses the FreeType Project (https://freetype.org), under the FreeType License. Build it with `just web-local-fonts-build` using Emscripten with the reviewed FreeType 2.14.3/zlib 1.3.2 ports. `just web-build` includes this step and stages the result for `just web-run --port 8787`.
