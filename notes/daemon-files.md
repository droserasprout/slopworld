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
| `api/` | Routing, HTTP/Protobuf boundaries, and WebSocket transport. `handlers/actions.rs` owns explicit launches and file actions; `handlers/config.rs` owns config patch policy. See [file mutation boundary](daemon-file-mutations.md). |
| `session/` | Session types, agent-template definitions, input, validation, wire views. `events.rs` owns published events and their shared encoding cache; `protobuf.rs` owns wire conversion. |
| `session/manager/` | `Manager` and its guards, configuration synchronization, session lifecycle, capture, task-store ownership, workers. |
| `session/manager/projects.rs` | Project catalog edits and checkout relocation coordination. `directories.rs` owns newly created directories until commit. |
| `session/manager/lifecycle/` | Start, stop, adoption, and reconciliation of configured sessions. |
| `session/manager/capture/` | Terminal readers, input, frames, scrollback, and title capture. |
| `session/manager/init.rs`, `maintenance.rs` | Startup recovery and maintenance scheduling. Configuration transactions stay in `manager/config/mod.rs`; `lifecycle/reconcile.rs` applies them to live sessions. |
| `clock.rs`, `paths.rs` | Unix-millisecond timestamps, filesystem metadata, and atomic file writes. Latency measurements use their own monotonic clock in `latency.rs`. |
| `process.rs` | Shared bounded child capture, timeout, kill, and reap mechanics. |
| `emu/`, `tmux.rs`, `tmux/` | Terminal mirror and tmux transport. `tmux/server.rs` owns server startup and readiness; `emu/serialize.rs` owns cell-to-row encoding; `tmux/control.rs` decodes control-mode output before bytes enter the mirror. |
| `sandbox/`, `presets.rs`, `presets/edit.rs` | Sandbox construction, preset snapshots, and serialized catalog mutations. |
| `config/` | Configuration model, persistence, validation, ownership and resolution. |
| `git/` | Git inspection and restricted command execution. |
| `worktrees/` | Independent worktree records and bounded Git operations. See [worktree ownership](daemon-worktrees.md). |
| `tasks.rs`, `grant.rs` | Durable mailboxes and scoped authority. |
| `audio/`, `jukebox.rs` | Playback and station catalog. `session/manager/music.rs` owns playback selection and serialization for every transport; its private `music/ncspot.rs` owns the Spotify terminal, IPC and recovery metadata. |
| `usage/` | Provider polling and quota normalization. |
| `bin/` | Launcher, installer, and `slopctl` CLI. |

See [session state](daemon-session-state.md), [sandbox isolation](sandbox-isolation.md),
[configuration stores](daemon-config-stores.md), [agent templates](daemon-agent-templates.md),
and [workers](daemon-workers.md) for the contracts that edits must preserve.

Audio request identity and publication belong to `audio/control.rs`. `worker.rs`
coordinates commands and opener completions; `playback.rs` prepares output and
starts feeders; `ring.rs` owns callback buffering; `title.rs` serializes candidate
metadata activation. Publication locks request then state after playback I/O.
Title activation holds its phase lock before entering that publication boundary.
Final-handle cleanup cancels owned opens and feeders without advancing the global
source generation; dropping an idle player must not retire another player.
