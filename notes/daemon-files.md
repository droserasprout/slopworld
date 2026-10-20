# Daemon source map

Paths below are relative to `slopd/src/`. This map identifies subsystem boundaries;
module declarations own the current file inventory. Modules with children use a directory `mod.rs`, except presets
and Cargo binary entry points. Test and coverage guidance belongs to
[build commands](build-commands.md).

| Area | Responsibility |
| --- | --- |
| `main.rs` | Startup/service orchestration and token middleware. |
| `shared/` | Protocol constants/enums and serialization helpers. |
| `api/` | Routing, HTTP/Protobuf boundaries, and WebSocket transport. `handlers/actions.rs` decodes explicit launches and file actions; `handlers/config.rs` owns config patch policy. See [file mutation boundary](daemon-file-mutations.md) and [file preview boundary](daemon-file-previews.md). |
| `api/handlers/files/`, `api/handlers/highlighting.rs` | File and syntax-preview HTTP decoding, path resolution, error mapping, and wire projection. |
| `files/` | Transport-independent browsing, file-action path/command preparation, mutations, preview containment and reader stamps, bounded ripgrep search, and highlighter execution. `crate::git` retains Git inspection ownership. |
| `session/launch.rs` | Common launch requests, execution source, destination, launch overrides and reader context. Live sessions own UI intent and reader identity separately from saved agent definitions; startup snapshots them and adoption restores them from tmux metadata. |
| `session/` | Session types, agent-template definitions, input, validation, wire views. `events.rs` owns published events and their shared encoding cache; `protobuf.rs` owns wire conversion. |
| `session/validation.rs` | Generated-name normalization shared by library and host sessions; each caller chooses its empty-name fallback. |
| `session/manager/` | `Manager` and its guards, configuration synchronization, session lifecycle, capture, task-store ownership, workers. |
| `session/manager/projects.rs` | Project edits and relocation; `directories.rs` owns uncommitted directories. See [worktree ownership](daemon-worktrees.md). |
| `session/manager/launch.rs` | Explicit launch preparation and guarded execution; saved library entries and music use the same launch request. |
| `session/manager/lifecycle/` | Start, stop, adoption, and reconciliation of configured sessions. |
| `session/manager/capture/` | Terminal readers, input, frames, scrollback, and title capture. |
| `session/manager/host_metadata.rs` | Host-shell record edits, working-directory/process observations, and serialized background tmux polling. See [host tabs](daemon-host-terminals.md). |
| `session/manager/init.rs`, `maintenance.rs` | Startup recovery and maintenance scheduling. The `config/` adapter publishes accepted configuration and auth; `lifecycle/reconcile.rs` applies configuration to live sessions. `catalog_state.rs` owns reload scheduling state. |
| `clock.rs`, `paths.rs` | Unix-millisecond timestamps, filesystem metadata, and atomic file writes. Latency measurements use their own monotonic clock in `latency.rs`. |
| `storage/workspace.rs`, `storage/document.rs` | Shared ordered-record discovery, preparation and publication, and TOML extension mechanics. Session/project/checkout schemas retain their own validation and nested extension policy. |
| `storage_id.rs` | OS-random opaque storage IDs and syntax checks; each store owns reservation and collision exclusion. |
| `process.rs` | Shared bounded child capture, timeout, kill, and reap mechanics. |
| `emu/`, `tmux/` | Terminal mirror and tmux transport. `tmux/server.rs` owns server startup and readiness; `emu/serialize.rs` owns cell-to-row encoding; `tmux/control.rs` decodes control-mode output before bytes enter the mirror. |
| `sandbox/`, `presets.rs`, `presets/edit.rs` | Sandbox construction, preset snapshots, and serialized catalog mutations. |
| `config/` | Machine settings, assembled configuration view, domain validation and command resolution. `validation.rs` owns configuration/project invariants, `library.rs` owns library-item rules, and `patch.rs` converts JSON settings patches. `settings_document.rs` prepares root-only edits; `crate::storage` owns workspace records and recovery. |
| `config/transactions/` | Serialized preparation, recovery, disk commits, cache rollback, and accepted record/catalog indexes. `session/manager/config/` adapts session validation, guarded runtime publication, and announcements. |
| `git/` | Git inspection and restricted command execution; see [Git boundary](daemon-git.md). |
| `worktrees/` | Independent worktree records and bounded Git operations. See [worktree ownership](daemon-worktrees.md). |
| `tasks.rs`, `tasks/`, `grant.rs` | Mailbox model, indexed policy and persistence adapters; scoped authority. |
| `music/` | Playback selection, catalog resolution, recovery, and serialized source transitions. `ncspot.rs` owns managed-player state and bounded IPC; `session/manager/music.rs` adapts host-terminal lifecycle through `HostSessions`. |
| `audio/`, `jukebox.rs` | Decoded playback and station catalog storage. `tmux/ncspot.rs` owns managed-player and applied-volume markers. See [jukebox](mod-jukebox.md). |
| `usage/` | Provider polling and quota normalization. |
| `bin/` | Launcher, installer, and `slopctl` CLI. |
| `bin/slopctl/logs/stream.rs` | Bounded log fan-in and producer/child cleanup. |

See [session state](daemon-session-state.md), [sandbox isolation](sandbox-isolation.md),
[configuration stores](daemon-config-stores.md), [agent templates](daemon-agent-templates.md),
and [workers](daemon-workers.md) for the contracts that edits must preserve.
