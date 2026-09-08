# Maintainability hotspots plan

Prioritize coupling and change risk, not line count alone. The current inventory is a
read-only source scan from 2026-09-08: physical lines from `wc -l`, code lines from `tokei`,
with build output and assets excluded.

## First extraction pass

The first manager/API pass is complete. `Manager::start` now delegates to the focused startup
owner in `manager/start.rs`, while tmux orphan recovery and errand-session reservation live in
`manager/adoption.rs` and `manager/errands.rs`. Preset HTTP policy moved to
`api/handlers_presets.rs`; the main handler module retains shared guards and route-facing
façade exports. Public methods and event ordering were kept intact, and the moved worker
reconstruction test remains beside the adoption owner.

The existing `TerminalWindow` history, selection, link, and rendering services, `AgentSidebar`
layout model, usage/sandbox seams, and resource-specific HTTP modules remain the next owners to
touch only when a behavior change gives them a sharper boundary.

The follow-up pass has since put Manager's durable task mutex behind `manager/tasks.rs`, split
session/library/grant HTTP handlers into focused modules, moved persistent sandbox state into
`sandbox/state.rs`, and given usage polling, websocket frames, sidebar buckets, and terminal
links, host-terminal policy, and sandbox network/seeding their own implementation owners. The
remaining work below is the deeper lifecycle and rendering ownership, not another broad file
split.

## Risk order

1. **Manager ownership.** `Manager` spans roughly 4,000 lines of `impl` blocks across
   configuration, session lifecycle, capture, library, workers, tasks, and broadcasts.
   The startup, orphan-adoption, errand, worker, and durable-task owners are now extracted.
   Remaining work is the deeper configuration synchronization and shared live/session state;
   keep the public façade, preserve event ordering, and keep state-transition tests beside each
   owner.
2. **TerminalWindow.** The 15 partial definitions are easier to navigate but still share one
   stateful fullscreen coordinator. Link hit testing now has a `TerminalLinkService`; history,
   selection, and rendering still remain coupled to the fullscreen coordinator. Extract those
   services without adding more cross-partial state. Keep input ordering, resize negotiation,
   and stopped-pane behavior covered by focused tests.
3. **AgentSidebar.** Keep the Harmony adapter thin and make the row/layout model the source of
   drawing, hit-testing, and keyboard order. The layout model and bucket helpers now provide that
   source while preserving the parked-entry/index invariant and back/front draw timing; this
   hotspot's planned extraction is complete unless a behavior change exposes a sharper seam.
4. **HTTP handlers.** Split `api/handlers.rs` by resource or policy boundary while retaining
   shared authentication and error helpers. Session, library, grant, task, config, file, preset,
   clipboard, usage, and audio boundaries now live in focused modules; `handlers.rs` is the
   shared façade and test home.
5. **Sandbox.** `sandbox/mod.rs` is both a broad subsystem and a security boundary. Extract only
   seams with clear ownership and tests; state, host-terminal policy, network/seeding, and
   bind policy/mounts now have separate owners. The remaining `mod.rs` code is argv orchestration,
   preset resolution, and its boundary tests. Keep refused-path checks and ordered argv
   construction central. Do not optimize for file size at the expense of reviewability.

## Secondary hotspots

`audio/mod.rs`, `bin/slopworld.rs`, `session/mod.rs`, and `emu.rs` are large but mostly cohesive.
`usage.rs` and `sandbox/bind.rs` now have parsing/provider/scheduler and policy/mount seams;
revisit them only when a behavior change exposes a sharper boundary. `MenuBackgroundBake.cs`,
`AboutPage.cs`, and `config/model.rs` are low-priority size outliers.

## Long-method queue

The queued extractions for `Manager::start`, `Manager::adopt_orphans`,
`Manager::create_errand_session`, `usage::spawn`, `AgentSidebar.Bucket`, and
`MiniWebSocket.ReadLoop` are complete. The next long-method reviews are the TerminalWindow
history/rendering coordination and Manager configuration synchronization when their behavior is
touched. A long method that is pure formatting or serialization is a lower-risk candidate than
one coordinating lifecycle or framework state.

## Refactor guardrails

1. Extract one stable concept at a time; do not refactor by line count alone.
2. Keep security, lifecycle, UI event-ordering, and state-transition tests with their owner.
3. Run `make format`, `make test`, and `make lint` after each behavior-preserving extraction.
4. Refresh this note when ownership or hotspot rankings materially change.
