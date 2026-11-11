# slopweb

Local browser Eco workspace using the installed Waylith/RimWorld 1.7 game and
SlopWorld mod in WebAssembly. No colony is created or loaded. Agent processes,
tmux, sandboxes, and daemon credentials stay on the host.

## Install and run

From a checkout with the .NET 10 SDK, Node.js 22+, Python/uv, Just, and a local
Waylith/RimWorld installation:

```sh
just slopweb-install
slopweb
# Open http://127.0.0.1:8787/
```

Source installation defaults to `~/.local`; ensure `~/.local/bin` is on PATH.
Pass a prefix to `just slopweb-install PREFIX` or `just slopweb-uninstall PREFIX`.
Uninstall preserves private runtime caches and daemon configuration.

Installed packages require the .NET 10 runtime, Node.js 22+, Python 3.11+, and Fontconfig;
they do not need the SDK, Just, uv, or a checkout. Start slopd first. The launcher
reads its host-side local endpoint descriptor, respecting `SLOPD_CONFIG_ROOT`
and `SLOPD_ENDPOINT`. `SLOPD_URL` and `SLOPD_TOKEN` override that descriptor.
Use a root token for session creation and configuration. `--endpoint URL` and
`--token-file PATH` also select a host-side connection. Tokens are never placed
in browser assets or URLs.

`--game PATH` (or `RIMWORLD`) selects another game installation. `--port` defaults
to 8787. First launch prepares local game files under `$XDG_CACHE_HOME/slopweb`
(default `~/.cache/slopweb`); subsequent launches reuse that private runtime.
`--prepare` prepares without serving; `--rebuild` refreshes after game data changes.
The server binds only to 127.0.0.1. Stop it with Ctrl+C; agents continue running.
From an agent sandbox, `just slopweb-host-run` starts the launcher on the host.
Interactive invocations use a controlling terminal so Ctrl+C also stops the
host service; noninteractive invocations retain piped output.
The printed root URL applies desktop defaults without redirecting. Preparation
imports only allowlisted appearance preferences from the local profile and bakes
its UI and terminal fonts through the installed native rasterizer. Font files and
atlases stay in the private cache; changed appearance preferences select a new
cache. Use `--rebuild` after changing installed font files.

## Optional mixed AOT

The default runtime uses the interpreter. Pass `--aot-sdk PATH` to both preparation
and serving to select mixed AOT instead:

```sh
slopweb --aot-sdk /private/dotnet/dotnet --prepare
slopweb --aot-sdk /private/dotnet/dotnet
```

PATH selects an existing .NET 10 SDK directory or its `dotnet` executable with
matching `wasm-tools`, including the WebAssembly SDK and browser cross compiler
packs for that SDK's runtime patch. The launcher validates the toolchain and pins
the selected SDK and target runtime for publishing. It never installs tools.
Initial compilation requires substantial time, disk and memory; the launcher
prints the private cache and publish log paths. NuGet restore may download the
project's pinned package dependencies.

Compilation is reused for identical final patched DLLs, authored compiler inputs,
settings, and SDK/runtime identity. Font, appearance, and JavaScript
changes compose a new runtime using the existing compiled framework. Failed or
interrupted publishes resume in the same private compilation directory on retry.
Only successful complete runtimes are served, and existing serving roots survive
refreshes. `--rebuild` refreshes game preparation; unchanged compiler inputs still
reuse compiled AOT. All compilation output and logs stay under `--cache` in
`aot-builds/`; immutable serving roots live in `aot-runtimes/`.

Mixed AOT retains application IL and reflection metadata, roots all application
assemblies, and keeps Waylith, Waylith.Injection, and
RimWorldWaylith.PrepatcherCompat interpreted. Its loader resolves published
assembly identities by name; the `skipLogger` alternate DLL option is disabled.

## Packages

`just slopweb-build` builds without an installed game, using pinned RimRef
assemblies for compilation. These references never enter the package. RimWorld
is required only when the installed launcher prepares and runs the workspace.

```sh
just package-web arch
just package-web debian
just package-web fedora
# Equivalent: just package-product DISTRIBUTION slopweb
```

For an already-built bundle, `just slopweb-package-staged DISTRIBUTION VERSION REVISION`
creates a local archive without rebuilding the private development runtime. Pass
a Git revision (or `HEAD`) for reproducible package timestamps. This does not
publish or install the archive. `just slopweb-test` inspects the newest local Arch
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
Covered
text, including styled and wrapped runs, uses native font metrics and
atlases. Unsupported sizes and characters still use approximate rendering.
Atlases are prepared at densities 1, 5/3 and 2 and resampled at other display scales.
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
changes load native host fonts on demand after a short debounce. Existing fonts
remain usable during preparation; a failed selection restores the previous settings
and offers retry. The private cache and retained atlas memory are bounded; reload
the page if the atlas budget is exhausted. Covered characters include Latin-1,
Cyrillic and U+2000–U+27FF punctuation/symbols where the selected face supports them.
Repeated page-network interruption and terminal recovery have browser checks;
daemon restart, run replacement, and the full agent launch/edit dialog have not
been exhaustively validated. See the checkout's
`experiments/browser/README.md` for focused checks and implementation boundaries.

The standalone release installer also offers `slopweb` on x86-64 Linux:

```sh
python3 install.py --install slopweb
python3 install.py --remove slopweb
```

Installation requires a published release containing the matching `slopweb` package
and checksum. For locally built preview packages, use the distribution package manager.
