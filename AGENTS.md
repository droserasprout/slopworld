
# SlopWorld

This project is a RimWorld mod (C#, Harmony) and `slopd` daemon (Rust).

Keep this file concise. Update its curated links when notes are added, moved, or renamed; keep the complete list in `notes/index.md`. Keep short notes in separate files in `notes/` and read/update them when needed.

Essentials:

- Use `make` for all project commands.
- [overview](notes/overview.md) — what this is
- [index](notes/index.md) — all devnotes
- [build-commands](notes/build-commands.md) — make, format, debug
- [duplication-refactor-plan](notes/duplication-refactor-plan.md) — static-analysis findings and refactor order
- [paths](notes/paths.md) — config, logs, tmux socket
- [config-stores](notes/config-stores.md) — daemon & mod config files
- [wire-protocol](notes/wire-protocol.md) — WS events & HTTP routes
- [gotchas](notes/gotchas.md) — the traps
- [house-rules](notes/house-rules.md) — commit rules & note discipline

Architecture:

- [daemon-files](notes/daemon-files.md) — slopd/src/*.rs layout
- [daemon-session-state](notes/daemon-session-state.md) — state machine, clocks, emulator
- [daemon-workers](notes/daemon-workers.md) — task-owned child workers and lifecycle
- [daemon-presets](notes/daemon-presets.md) — sandbox argv & presets
- [sandbox-isolation](notes/sandbox-isolation.md) — bind guard, private state
- [mod-client](notes/mod-client.md) — Client/: hub, socket, config mirror
- [mod-sim](notes/mod-sim.md) — Sim/: colony reconcile, clock, intro
- [mod-ui-chrome](notes/mod-ui-chrome.md) — shared widgets, layout, top bar
- [mod-ui-widgets-refactor](notes/mod-ui-widgets-refactor.md) — staged widget consolidation plan
- [mod-sidebar](notes/mod-sidebar.md) — AgentSidebar & colonist bar patching
- [mod-terminal](notes/mod-terminal.md) — terminal pane: rendering, keys, theme
- [mod-terminal-history-warmup](notes/mod-terminal-history-warmup.md) — first-scroll history warm-up plan
- [mod-jukebox](notes/mod-jukebox.md) — the jukebox & clanker soundtrack
