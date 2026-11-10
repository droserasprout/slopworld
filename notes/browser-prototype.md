# Browser prototype boundary

[`experiments/browser/`](../experiments/browser/README.md) owns the local-only
Waylith/RimWorld WASM investigation. `just/browser.just` exposes its checks.
Production mod startup, daemon runtime, and packaging remain independent.
Installed assemblies, derived assets, saves, and reports stay in ignored output.

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

BrowserHttpPatch adapts staged HTTP construction/base URL. BrowserSocketPatch
substitutes asynchronous browser WebSockets for raw sockets. Actual DaemonClient
admission/deadlines/completion pumping, HubTransport serial checks/bounded queues,
SessionHub catalogs, and TerminalIO subscriptions retain ownership. Overload closes
the socket rather than blocking the browser thread. Shutdown flush reports false
while a write is pending. [Client](mod-client.md) and [wire](protocol-wire.md)
contracts still apply.

## Entry workspace and GUI

BrowserWorkspace pumps engine time, client completions, SessionHub, and the real
WindowStack without colony updates. Waylith's deferred GUI window input/drawing
callbacks remain necessary. Never invoke the baseline executable-entry diagnostic
after workspace startup: it resets engine globals. Native rejection messages draw
after deferred windows so the terminal cannot cover them.

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
null map selection in Entry. Ordinary metadata joins session-only row eligibility
without changing SessionInfo or wire data. BrowserModContent mounts authored
textures/themes after Core startup using the real ModContentPack/content holder.
Host PNG decoding supplies readable Unity pixels; ContentFinder retains lookup.

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
approximate; rich text/wrapping, overlap routing, and material/primitive drawing
remain incomplete.

Render textures retain Canvas backing across frames for the actual mod terminal
cache. Browser capability reporting disables GPU CopyTexture so that cache selects
its full-screen repaint branch. Image uploads follow native identity and
Apply/LoadImage revisions; subsequent commands reference uploaded images rather
than copying an atlas per glyph. Discarded input/layout uploads must also discard
their managed cache entries. Weak ownership and release commands retire native
resources. Pixel conversion reverses Unity's bottom-up rows using bulk copies.
Every frame's commands must reach Canvas, including initial uploads later frames use.

BrowserGuiInput/gui-input.js dispatch ordered DOM events through installed controls.
UTF-16 units stay in one batch until the actual terminal input flush. Modifier
shortcuts use physical keys; ordinary text does not duplicate physical keydown.
Printable events retain their physical key on the same text-bearing event so native
binding capture can identify a letter without generating a second character.
Focus loss clears native button capture as well as GUI focus. Clipboard and full IME
remain incomplete. BrowserInput/input.js separately feed Waylith and Unity device
state. The end-to-end harness sends DOM input only into its newly created temporary
host shells, observes the actual SessionHub ScreenBuf, and removes only those
shells. Existing coding-agent attachment is read-only; host credentials remain
outside diagnostic scripts and reports.

Workspace runs disable managed debugging by default so Mono's Jiterpreter can run.
`managedDebug` is an explicit diagnostic override. This is independent of Debug
versus Release build configuration.

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
