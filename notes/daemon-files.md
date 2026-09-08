# Daemon source map

Paths below are relative to `slopd/src/`. Use module declarations for the current
file inventory; this map identifies subsystem boundaries.

| Area | Responsibility |
| --- | --- |
| `main.rs` | Startup, retick loop, token middleware. |
| `api/` | Router, HTTP guards and handlers, WebSocket transport. |
| `session/` | Session types, input, templates, validation, wire views. |
| `manager/` | Configuration synchronization, session lifecycle, capture, tasks, workers. |
| `emu.rs`, `tmux.rs` | Terminal mirror and tmux transport. |
| `sandbox/`, `presets.rs` | Sandbox construction and preset resolution. |
| `config/`, `config.rs` | Configuration model, persistence, validation, inheritance. |
| `tasks.rs`, `grant.rs` | Durable mailboxes and scoped authority. |
| `audio/`, `jukebox.rs` | Playback and station catalog. |
| `usage/`, `usage.rs` | Provider polling and quota normalization. |
| `bin/` | Launcher, installer, and `slopctl` CLI. |

See [session state](daemon-session-state.md), [sandbox isolation](sandbox-isolation.md),
[configuration stores](config-stores.md), and [workers](daemon-workers.md) for the
contracts that edits must preserve.
