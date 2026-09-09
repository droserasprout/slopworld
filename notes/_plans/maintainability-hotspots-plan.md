# Maintainability hotspots plan

Track work by coupling and change risk, not line count alone.

## Remaining work

### 1. Manager configuration and shared session state

The remaining coupling is concentrated in `slopd/src/manager/config.rs` and the `Manager` state
declared in `slopd/src/session/ctrl.rs`.

`manager/config.rs` still combines:

- config persistence, reload stamps, auth invalidation, and preset/jukebox reloads;
- config-to-live reconciliation, host-terminal upserts, manifest/title synchronization, and
  autostart;
- client/watch bookkeeping, usage publication, redraw requests, and config patch/replace flows.

Extract one owner at a time behind the existing `Manager` façade. Start with persistence/reload
state and its stamps, then consider a config-to-live reconciliation owner. Keep `live`, `cfg`,
rules, and event publication ordering explicit; do not merge lifecycle or worker behavior into a
generic “manager state” object.

Required coverage includes invalid-config retry behavior, root-token invalidation, host-terminal
and worker handling, autostart ordering, manifest/title synchronization, client/watch guard
release, and usage/redraw broadcasts.

### 2. TerminalWindow coordination

The fullscreen coordinator owns cross-cutting history, selection, scrolling, and rendering state
across these partial definitions:

- `TerminalWindow.History.cs` — history caches, request replies, displayed-frame selection, and
  live-frame translation;
- `TerminalWindow.Selection.cs` — offset translation and selection text/model coordination;
- `TerminalWindow.Rendering.cs` — screen/selection painting and history-bar UI;
- `TerminalWindow.Scrolling.cs` — scroll negotiation and request planning.

The existing history cache, selection state/input, run cache, and link hit-testing helpers are
useful primitive owners. The next extraction should own coordination around those primitives,
not add another façade of aliases:

1. Move history lifecycle state and request/display decisions behind a history coordinator while
   keeping daemon offsets and stale-reply handling observable and tested.
2. Move selection offset translation and text extraction behind a selection coordinator, leaving
   gesture event ordering with the input controller.
3. Move screen painting and history-bar rendering behind a renderer that receives explicit frame,
   geometry, and selection inputs; keep cache invalidation and resize negotiation behavior intact.

Preserve input ordering, fractional scroll anchoring, resize negotiation, stopped-pane fallback,
link activation, and selection clipboard behavior. Do not run the game or take screenshots as part
of this refactor.

### 3. Secondary review

These are large but currently cohesive. Revisit them only when a behavior change exposes a clear
boundary:

- `slopd/src/audio/mod.rs`
- `slopd/src/bin/slopworld.rs`
- `slopd/src/session/mod.rs`
- `slopd/src/emu.rs`
- `mod/Source/SlopWorld/UI/MenuBackgroundBake.cs`
- `mod/Source/SlopWorld/UI/Settings/AboutPage.cs`
- `slopd/src/config/model.rs`

## Review queue

When touching the surrounding behavior, review `Manager::reload_if_changed`,
`Manager::sync_from_config`, `Manager::patch_config`, `Manager::replace_config`,
`TerminalWindow.DisplayedScreen`, `TerminalWindow.NoteLiveFrame`, and the rendering `Paint`
methods first. Prefer a pure formatting/serialization extraction over a lifecycle change only
when it gives a genuinely narrower owner.

## Refactor guardrails

1. Extract one stable concept at a time; do not refactor by line count alone.
2. Keep security, lifecycle, UI event-ordering, and state-transition tests with their owner.
3. Preserve public façades and event ordering while making ownership explicit.
4. Run `make format`, `make test`, and `make lint` after each behavior-preserving extraction.
5. Refresh this note when ownership or hotspot rankings materially change.
