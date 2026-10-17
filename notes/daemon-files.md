# Daemon source map

Paths below are relative to `slopd/src/`. This map identifies subsystem boundaries;
module declarations own the current file inventory. Modules with children use a directory `mod.rs`, except presets
and Cargo binary entry points. Test and coverage guidance belongs to
[build commands](build-commands.md).

| Area | Responsibility |
| --- | --- |
| `main.rs` | Startup/service orchestration and token middleware. |
| `shared/` | Protocol constants/enums and serialization helpers. |
| `api/` | Routing, HTTP/Protobuf boundaries, and WebSocket transport. `handlers/actions.rs` owns explicit launches and file actions; `handlers/config.rs` owns config patch policy. See [file mutation boundary](daemon-file-mutations.md) and [file preview boundary](daemon-file-previews.md). |
| `api/handlers/files/` | Browsing, previews, mutations, Search, and Git status HTTP boundaries have separate owners. `search.rs` runs bounded `rg` output with UTF-8-safe previews; `api/handlers/highlighting.rs` owns highlighter styling, previews, and execution. |
| `session/` | Session types, agent-template definitions, input, validation, wire views. `events.rs` owns published events and their shared encoding cache; `protobuf.rs` owns wire conversion. |
| `session/validation.rs` | Generated-name normalization shared by library and host sessions; each caller chooses its empty-name fallback. |
| `session/manager/` | `Manager` and its guards, configuration synchronization, session lifecycle, capture, task-store ownership, workers. |
| `session/manager/projects.rs` | Project edits and relocation; `directories.rs` owns uncommitted directories. See [worktree ownership](daemon-worktrees.md). |
| `session/manager/lifecycle/` | Start, stop, adoption, and reconciliation of configured sessions. |
| `session/manager/capture/` | Terminal readers, input, frames, scrollback, and title capture. |
| `session/manager/host_metadata.rs` | Host-shell record edits, working-directory/process observations, and serialized background tmux polling. See [host tabs](daemon-host-terminals.md). |
| `session/manager/init.rs`, `maintenance.rs` | Startup recovery and maintenance scheduling. Configuration transactions stay in `manager/config/mod.rs`; `lifecycle/reconcile.rs` applies them to live sessions. |
| `clock.rs`, `paths.rs` | Unix-millisecond timestamps, filesystem metadata, and atomic file writes. Latency measurements use their own monotonic clock in `latency.rs`. |
| `storage/workspace.rs`, `storage/document.rs` | Shared ordered-record discovery, preparation and publication, and TOML extension mechanics. Session/project/checkout schemas retain their own validation and nested extension policy. |
| `storage_id.rs` | OS-random opaque storage IDs and syntax checks; each store owns reservation and collision exclusion. |
| `process.rs` | Shared bounded child capture, timeout, kill, and reap mechanics. |
| `emu/`, `tmux/` | Terminal mirror and tmux transport. `tmux/server.rs` owns server startup and readiness; `emu/serialize.rs` owns cell-to-row encoding; `tmux/control.rs` decodes control-mode output before bytes enter the mirror. |
| `sandbox/`, `presets.rs`, `presets/edit.rs` | Sandbox construction, preset snapshots, and serialized catalog mutations. |
| `config/` | Machine settings, assembled configuration view, validation and resolution. `settings_document.rs` prepares root-only edits; `storage/` owns workspace records and recovery. |
| `git/` | Git inspection and restricted command execution; see [Git boundary](daemon-git.md). |
| `worktrees/` | Independent worktree records and bounded Git operations. See [worktree ownership](daemon-worktrees.md). |
| `tasks.rs`, `tasks/`, `grant.rs` | Mailbox model, indexed policy and persistence adapters; scoped authority. |
| `audio/`, `jukebox.rs` | Playback and station catalog. `session/manager/music/` owns source selection and ncspot lifecycle. See [jukebox](mod-jukebox.md). |
| `usage/` | Provider polling and quota normalization. |
| `bin/` | Launcher, installer, and `slopctl` CLI. |
| `bin/slopctl/logs/stream.rs` | Bounded log fan-in and producer/child cleanup. |

See [session state](daemon-session-state.md), [sandbox isolation](sandbox-isolation.md),
[configuration stores](daemon-config-stores.md), [agent templates](daemon-agent-templates.md),
and [workers](daemon-workers.md) for the contracts that edits must preserve.
