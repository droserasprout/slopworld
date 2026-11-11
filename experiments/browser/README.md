# Local SlopWorld browser experiment

The Eco workspace executes installed Waylith/RimWorld 1.7 assemblies and the actual
SlopWorld mod locally in .NET WebAssembly. It starts in Entry and **does not create
or load a colony**. The actual sidebar, session rows, terminal output, menus, and
mod settings render through installed Unity GUI controls into Canvas2D.
`slopd`, agents, tmux, sandboxes, and credentials remain on the host.

This is a work-interface prototype, not streaming or a substitute dashboard.
The verified workflow creates temporary host shells through the actual sidebar,
types commands through native terminal controls, receives their output, switches
between sessions, and attaches to an existing coding agent. Existing coding agents
are observed read-only by the reusable harness; command execution is tested in
disposable shells using the same terminal path. An additional local check typed a
unique draft into this task's live coding agent, observed it in the actual terminal
buffer, and erased it without submitting. No colony is loaded during these checks.

## Manual testing

For an installed command and native packages, see [slopweb](../../slopweb/README.md).
`just slopweb-install` installs the independent launcher; `slopweb` prepares local
game data and serves the workspace. Packages never contain those game files.

Requirements: repository .NET 10 SDK, Just, Node.js with built-in WebSocket support
(tested with Node 26), Python/uv, Chromium for automated checks, and a local RimWorld
1.7 installation. First restore requires network access. No additional .NET workload
was needed for this interpreter build.

The [private mixed AOT experiment](aot/README.md) offers separate opt-in recipes
for an existing wasm-tools SDK and final prepared root. Its expensive build and
game-derived output stay isolated from launcher defaults and release packaging.

Configure `SLOPD_URL` and `SLOPD_TOKEN` in the host server environment. Use a root
token for session creation and daemon configuration; agent tokens retain their
restrictions. Never place credentials in browser files or URLs. From the repo root:

```sh
just browser-workspace-run
# Open http://127.0.0.1:8787/index.html?entry&assets&eco&workspace&live&app
```

An existing live session is currently required. Startup loads Core, mounts mod UI
content, enables Eco, connects the actual SessionHub, and opens its first live
terminal. Sidebar rows select sessions; the gear opens actual mod settings. The
`+` menu's Host shell action uses the actual API and displays rejection messages.
Close the tab and stop the serving recipe with Ctrl+C when finished; host agents
continue running.

The manual page fits a 1280×720 surface to the viewport. Remove `app` for the
diagnostic report below the canvas. `managedDebug` enables managed debugging but
disables the Jiterpreter and is much slower. Set `RIMWORLD=/absolute/game/directory`
to select another installation and `CHROMIUM=/path/to/chromium` for checks.
The harness uses SwiftShader; results do not measure hardware GPU performance.

## Validation

```sh
just browser-workspace
# Thirty actual workspace frames, live output, Entry/no-game/world/map assertions.

just browser-workspace-live
# Prints a retained diagnostic browser PROFILE and URL.
just browser-profile PROFILE experiments/browser/artifacts/baseline-observe.json 15
# Read-only observation: Chrome CPU profile, frame intervals, renderer task CPU,
# JS heap, viewport/DPI, browser version and workspace state. Repeat with identical
# session output and configuration. This is not a controlled idle/input benchmark;
# JS heap excludes WASM memory, and CPU sampling adds measurement overhead.
# Use browser-workspace-profile instead of browser-workspace-live to enable
# bounded per-frame native input/layout/repaint/drain, managed allocation/GC,
# JSON parse and Canvas timings. The same browser-profile command collects them.
just browser-serialization-check PROFILE
# Compare generated/reference JSON writers on one identical live drawing packet
# inside WASM. Verifies byte equivalence and alternates measurement order.
just browser-test-commands
# Game-free coverage of all wire payload types, escaping and numeric coordinates.
just browser-host-inspect
# Run read-only native process/path inspection through the host user service manager.
just browser-native-reference
# Capture through the host desktop screenshot portal (approve its dialog if shown).
# Requires host PyGObject; writes the local native-reference.png without game input.
just browser-workspace-reference SESSION
# Host-backed reference at 2560x1440, 5/3 display scale, using an allowlisted copy
# of local profile appearance preferences and read-only selection of a live session.
# This matches the captured host display; browser/native font and portrait parity
# are still incomplete. The host profile is never modified.
just browser-host-serve
# Serve the prepared runtime on port 8787 using the host local endpoint descriptor.
# This deliberately uses host-mode configuration rather than an inherited agent token.
just browser-host-url-check
# Stable bookmark after native font/reference preparation:
# http://127.0.0.1:8787/workspace.html?session=SESSION
just browser-host-benchmark PROFILE experiments/browser/artifacts/workloads.json
# Two invocation-owned temporary host sessions run benchmark-fixture.py. Records
# fixed idle/output/command-typing/keystrokes/scrolling/switching workloads, stage timings, frame and
# latency distributions, renderer task CPU, JS heap and per-phase screenshots.
# Restores the original session and verifies fixture deletion. Switching measures
# the native Open/attach path, not a DOM sidebar click. JS heap excludes WASM memory.
# Firefox uses a fresh owned profile through installed geckodriver (no extra SDK).
just browser-host-firefox-benchmark
# Pass the final headless argument as 0 for a visible hardware-rendered comparison;
# see harness/firefox-README.md for arguments, backend reporting and recovery.
just browser-workspace-ui-check PROFILE
just browser-host-workspace-ui-check PROFILE
# Checks settings, key rebinding, F9 layout toggling, and F12 close/reopen.
# Uses native-font label observations and native settings-door geometry.
# Requires authored D/F9/F12 bindings and the default open-files panel layout.
# Restores presentation preferences and the tested binding on failure as well.
just browser-workspace-e2e-check PROFILE
just browser-host-workspace-e2e-check PROFILE
# Requires a root-authorized bridge and an existing live coding agent.
# Creates two temporary host shells through the sidebar, checks command output,
# Backspace/Enter and session switching, attaches to the agent read-only, and
# removes only the shells created by this check and restores selection.
# Native-font menu targeting uses opt-in label observations; session clicks use
# native row identity and clipped scroll geometry. Host titles need not be unique.
just browser-host-clipboard-check PROFILE
just browser-host-image-paste-check PROFILE
# Trusted Chromium text/Unicode paste and native selection copy with prepared fonts.
# Image check clones a temporary Codex agent, attaches one PNG without submission,
# restores the previous selection/clipboard and deletes only its own fixture.
just browser-host-reconnect-check PROFILE
# Reloads this diagnostic page with transport tracking, interrupts its network twice,
# checks offline UI updates and recovered terminal echoes, and deletes its fixtures.
# The host daemon remains running. Results are saved in artifacts/reconnect-check.json.
just browser-host-composition-check PROFILE
# Chromium IME preedit/update, Unicode commit, cancellation and ordinary typing
# reach disposable terminals. The fixture reports exact received UTF-8 bytes.
# Results are saved in artifacts/composition-check.json; full OS IME is not covered.
just browser-close PROFILE

just browser-gui
# Native controls, modifiers/UTF-16, clipping/matrices, texture orientation,
# image updates/releases, retained/recreated render targets.

just browser-mod-http
# Actual mod HTTP/protobuf, HubTransport snapshot, SessionHub and populated ScreenBuf.
just browser-bridge-check
just browser-transport
just browser-test-harness
# Host authentication, binary traffic, browser Origin checks and bridge isolation.

just browser-lint
```

Reports/screenshots remain in ignored `artifacts/`. `browser-evaluate PROFILE SCRIPT`
runs a local diagnostic script against a loaded runtime; `browser-screenshot PROFILE`
captures that page. Transport checks do not type into existing agents.

The workspace checks show no Current.Game, world, map, or simulation ticks while
terminal/UI updates continue. UI clocks advance independently of simulation ticks.
Settings navigation, key rebinding, and a checkbox change/restore pass through DOM
events. F9 changes layout; F12 returns from settings and closes/reopens the terminal.
The end-to-end check observes the actual mod ScreenBuf for unique output markers
that cannot be satisfied by command echo. It also checks output after switching
back to each shell and verifies that test sessions were removed.
Persistent image uploads and the native terminal cache avoid
repeating complete symbol atlases per glyph. One local sample measured 20–27 ms for
cached managed frames and 131 ms for a full terminal repaint; this is not a frame-rate
guarantee.

## Remaining limits

- Early loading-screen and background parity are required alongside the workspace.
  The current pre-WASM page still uses diagnostic HTML. Derived assets must be baked
  from the local installation into the private cache; they must not enter packages.

- Reconnect recovery and the complete agent launch/edit configuration dialog remain
  unverified. Shell creation, terminal input/output, live-session switching, and
  attachment to an existing coding agent are verified. Startup currently requires
  at least one live session.
- Startup uses bounded offline hooks, not general Harmony. Actual ModEntry/profile
  loading and authored key definitions run; full ModBootstrap and simulation
  definitions are not initialized. Desktop-only shortcut actions remain unsupported.
- GUI metrics are approximate. Rich text/wrapping, overlapping-window hit routing,
  full IME and material/primitive drawing remain incomplete.
  Text copy/paste uses the browser clipboard; context-menu actions require browser
  clipboard permission. Session-only avatars use authored robot faceplates.
- Mod settings pages are present, but desktop system fonts, window-manager controls,
  and audio are not browser implementations. Profile settings use an in-memory
  filesystem and do not survive reload.
- Core startup reports known unavailable audio/profiler/font/cache services. Reports
  retain those errors. Successful workspace frames do not mean clean full engine
  startup. Avatar pawn graphics and map simulation are not required for this path.

## Separate engine probes

`runtime/FontProbe` is a host-only native font bake probe. It calls the installed
game's font configuration and line metrics and its ImGui/FreeType rasterizer without
starting a renderer or colony. For example:

```sh
just --justfile experiments/browser/runtime/justfile font-probe \
  /usr/share/fonts/TTF/ttf-google-fonts-typewolf/PT_Sans-Web-Regular.ttf \
  out/font-probe.json 15
```

The ignored output contains glyph advances, bounds, UVs, native line metrics, and
atlas pixels. This probe covers Latin-1, Cyrillic U+0400–U+052F, and U+2000–U+27FF and reports native fallback
use. The optional `nativeFonts` workspace path renders covered plain text from these
bakes; normal and styled native faces share the prepared family; complete Unicode fallback remains incomplete.
`browser-workspace-font-reference FONT SESSION SIZE` stages the selected UI family
and resolves the terminal family through the actual mod's host font catalog and
fontconfig order. Host font files and derived pixels stay in ignored local output.

Existing colony/rendering experiments remain independent of the Entry workspace:

| Recipes | Evidence |
| --- | --- |
| `browser-run`, `browser-check` | Baseline managed/game math and diagnostic shader grid |
| `browser-startup`, `browser-diagnose` | Original entrypoint and platform/Harmony failures |
| `browser-input`, `browser-gui` | Native input/GUI boundaries |
| `browser-lifecycle`, `browser-assets`, `browser-jobs` | Engine construction, mounting, serial native jobs |
| `browser-entry` | Core definitions and real Entry bootstrap |
| `browser-play`, `browser-save`, `browser-load` | Quick-test world/map, advancing ticks and save identity |
| `browser-eco` | Separate saved-colony Eco pause hooks |
| `browser-terrain-shaders`, `browser-terrain`, `browser-objects` | Partial terrain/object rendering |
| `browser-offline` | Actual background-preference prefix applied offline |
| `browser-draws`, `browser-textures`, `browser-scene` | Meshes, BC7 textures, engine scene pacing |
| `browser-webgpu` | One installed compute shader and verified readback |

These checkpoints do not establish a playable rendered colony. Native GLFW, Vulkan,
audio libraries, and custom WaylithCLR field injection remain unavailable as-is.
See [runtime](runtime/README.md), [graphics](graphics/README.md), and
[compatibility](compat/README.md) for focused evidence.

## Ownership and local-only output

`runtime/` owns assembly staging, adapters, WASM execution, and browser presentation;
`compat/Offline/` owns build-time hooks. `graphics/` and `webgpu/` own rendering
probes. `harness/` owns serving, bridge isolation, and Chromium validation.
[Ownership notes](../../notes/browser-prototype.md) record cross-boundary traps.
Browser adapters preserve daemon ownership; shared terminal selection geometry
lives in the mod. `slopweb/` owns local runtime
preparation and installation; `tools/release/slopweb.py` owns its package payload.

Only source and dependency locks belong in Git. Runtime/compatibility/graphics output
and `artifacts/` contain copied assemblies and derived local content.
**Do not publish or redistribute them.** The server binds to 127.0.0.1, confines access
to its build root, rejects escaping symlinks/foreign origins, and supplies browser
isolation headers. Host credentials enter only the upstream daemon connection.
