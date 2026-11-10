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

Requirements: repository .NET 10 SDK, Just, Node.js with built-in WebSocket support
(tested with Node 26), Python/uv, Chromium for automated checks, and a local RimWorld
1.7 installation. First restore requires network access. No additional .NET workload
was needed for this interpreter build.

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
just browser-workspace-ui-check PROFILE
# Checks settings, key rebinding, F9 layout toggling, and F12 close/reopen.
just browser-workspace-e2e-check PROFILE
# Requires a root-authorized bridge and an existing live coding agent.
# Creates two temporary host shells through the sidebar, checks command output,
# Backspace/Enter and session switching, attaches to the agent read-only, and
# removes only the shells created by this check. Use the default sidebar layout.
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

- Reconnect recovery and the complete agent launch/edit configuration dialog remain
  unverified. Shell creation, terminal input/output, live-session switching, and
  attachment to an existing coding agent are verified. Startup currently requires
  at least one live session.
- Startup uses bounded offline hooks, not general Harmony. Actual ModEntry/profile
  loading and authored key definitions run; full ModBootstrap and simulation
  definitions are not initialized. Desktop-only shortcut actions remain unsupported.
- GUI metrics are approximate. Rich text/wrapping, overlapping-window hit routing,
  clipboard, full IME, and material/primitive drawing remain incomplete.
- Mod settings pages are present, but desktop system fonts, window-manager controls,
  and audio are not browser implementations. Profile settings use an in-memory
  filesystem and do not survive reload.
- Core startup reports known unavailable audio/profiler/font/cache services. Reports
  retain those errors. Successful workspace frames do not mean clean full engine
  startup. Avatar pawn graphics and map simulation are not required for this path.

## Separate engine probes

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
Production daemon and mod source remain unchanged.

Only source and dependency locks belong in Git. Runtime/compatibility/graphics output
and `artifacts/` contain copied assemblies and derived local content.
**Do not publish or redistribute them.** The server binds to 127.0.0.1, confines access
to its build root, rejects escaping symlinks/foreign origins, and supplies browser
isolation headers. Host credentials enter only the upstream daemon connection.
