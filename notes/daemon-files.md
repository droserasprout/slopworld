# Daemon source map

Paths below are relative to `slopd/src/`. Use module declarations for the current
file inventory. This map identifies subsystem boundaries.

This is a selected subsystem map, not a file inventory. Module declarations own the
current inventory. Modules with children use a directory `mod.rs`, except presets
and Cargo binary entry points. Test and coverage guidance belongs to
[build commands](build-commands.md).

| Area | Responsibility |
| --- | --- |
| `main.rs` | Startup/service orchestration and token middleware. |
| `shared/` | Protocol constants/enums and serialization helpers. |
| `api/` | Routing, HTTP/Protobuf boundaries, and WebSocket transport. `handlers/actions.rs` owns explicit launches and file actions; `handlers/config.rs` owns config patch policy. See [file mutation boundary](daemon-file-mutations.md) and [file preview boundary](daemon-file-previews.md). |
| `api/handlers/files.rs` | File browsing, previews, Search routes, and Git status/count routes. Search runs bounded `rg` output with UTF-8-safe previews. |
| `session/` | Session types, agent-template definitions, input, validation, wire views. `events.rs` owns published events and their shared encoding cache; `protobuf.rs` owns wire conversion. |
| `session/manager/` | `Manager` and its guards, configuration synchronization, session lifecycle, capture, task-store ownership, workers. |
| `session/manager/projects.rs` | Project edits and relocation; `directories.rs` owns uncommitted directories. See [worktree ownership](daemon-worktrees.md). |
| `session/manager/lifecycle/` | Start, stop, adoption, and reconciliation of configured sessions. |
| `session/manager/capture/` | Terminal readers, input, frames, scrollback, and title capture. |
| `session/manager/init.rs`, `maintenance.rs` | Startup recovery and maintenance scheduling. Configuration transactions stay in `manager/config/mod.rs`; `lifecycle/reconcile.rs` applies them to live sessions. |
| `clock.rs`, `paths.rs` | Unix-millisecond timestamps, filesystem metadata, and atomic file writes. Latency measurements use their own monotonic clock in `latency.rs`. |
| `process.rs` | Shared bounded child capture, timeout, kill, and reap mechanics. |
| `emu/`, `tmux/` | Terminal mirror and tmux transport. `tmux/server.rs` owns server startup and readiness; `emu/serialize.rs` owns cell-to-row encoding; `tmux/control.rs` decodes control-mode output before bytes enter the mirror. |
| `sandbox/`, `presets.rs`, `presets/edit.rs` | Sandbox construction, preset snapshots, and serialized catalog mutations. |
| `config/` | Configuration model, persistence, validation, ownership and resolution. |
| `git/` | Git inspection and restricted command execution; see [Git boundary](daemon-git.md). |
| `worktrees/` | Independent worktree records and bounded Git operations. See [worktree ownership](daemon-worktrees.md). |
| `tasks.rs`, `grant.rs` | Durable mailboxes and scoped authority. |
| `audio/`, `jukebox.rs` | Playback and station catalog. `session/manager/music/` owns source selection and ncspot lifecycle. See [jukebox](mod-jukebox.md). |
| `usage/` | Provider polling and quota normalization. |
| `bin/` | Launcher, installer, and `slopctl` CLI. |
| `bin/slopctl/logs/stream.rs` | Bounded log fan-in and producer/child cleanup. |

See [session state](daemon-session-state.md), [sandbox isolation](sandbox-isolation.md),
[configuration stores](daemon-config-stores.md), [agent templates](daemon-agent-templates.md),
and [workers](daemon-workers.md) for the contracts that edits must preserve.
