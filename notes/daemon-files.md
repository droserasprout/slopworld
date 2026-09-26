# Daemon source map

Paths below are relative to `slopd/src/`. Use module declarations for the current
file inventory. This map identifies subsystem boundaries.

Rust unit tests live beside their owners in `*_tests.rs` (or `tests.rs` for `mod.rs`),
loaded through `#[cfg(test)]` and `#[path]` so module names and private access stay intact.
Binary-root tests use the binary's existing subdirectory: files directly in `src/bin/`
become Cargo executables. Keep test bodies and standalone fixtures in excluded test files.
Small instrumentation hooks can remain with production code. `make/config.mk` sets the
coverage scope.

| Area | Responsibility |
| --- | --- |
| `main.rs` | Startup, retick loop, token middleware. |
| `shared/` | Generated protocol, defaults and usage bindings, plus serialization helpers. |
| `api/` | Router, HTTP guards and handlers, WebSocket transport. |
| `session/` | Session types, agent-template definitions, input, validation, wire views. `events.rs` owns published events and their shared encoding cache; `protobuf.rs` owns wire conversion. |
| `session/manager/` | `Manager` and its guards, configuration synchronization, session lifecycle, capture, task-store ownership, workers. |
| `session/manager/init.rs`, `maintenance.rs` | Startup recovery and maintenance scheduling. Configuration transactions stay in `manager/config.rs`; `reconcile.rs` applies them to live sessions. |
| `process.rs` | Shared bounded child capture, timeout, kill, and reap mechanics. |
| `emu.rs`, `tmux.rs` | Terminal mirror and tmux transport. |
| `sandbox/`, `presets.rs` | Sandbox construction and preset resolution. |
| `config/`, `config.rs` | Configuration model, persistence, validation, ownership and resolution. |
| `worktrees.rs` | Independent worktree records and bounded Git operations. See [worktree ownership](daemon-worktrees.md). |
| `tasks.rs`, `grant.rs` | Durable mailboxes and scoped authority. |
| `audio/`, `jukebox.rs` | Playback and station catalog. `session/manager/music.rs` groups radio audio, ncspot state, and the playback transition lock. |
| `usage/`, `usage.rs` | Provider polling and quota normalization. |
| `bin/` | Launcher, installer, and `slopctl` CLI. |

See [session state](daemon-session-state.md), [sandbox isolation](sandbox-isolation.md),
[configuration stores](daemon-config-stores.md), [agent templates](daemon-agent-templates.md),
and [workers](daemon-workers.md) for the contracts that edits must preserve.
