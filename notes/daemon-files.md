# Daemon source map

Paths below are relative to `slopd/src/`. This map identifies subsystem boundaries;
module declarations own the current file inventory. Modules with children use a directory `mod.rs`, except presets
and Cargo binary entry points. Test and coverage guidance belongs to
[build commands](build-commands.md).

| Area | Responsibility |
| --- | --- |
| `main.rs` | Startup/service orchestration and token middleware. |
| `application/` | Startup-constructed accepted configuration/credential state, personal template and library catalogs, project coordination, and shared event/task/music services. Application owns catalog maintenance, worktree and file-action coordination, task summaries and their shared title cache, and usage/music publication; session adapters supply identity/authorization leases and host lifecycle; API owns event encoding. |
| `agent_templates/` | Portable template definitions, snapshot/version policy, and generation persistence. Application owns the accepted catalog; session adapters capture agents and commit application-prepared session drafts. |
| `shared/` | Protocol constants/enums and serialization helpers. |
| `api/` | Routing, HTTP/Protobuf boundaries, and WebSocket transport. `handlers/actions.rs` decodes explicit launches and file actions; `handlers/config.rs` owns config patch policy. See [file mutation boundary](daemon-file-mutations.md) and [file preview boundary](daemon-file-previews.md). |
| `api/handlers/files/`, `api/handlers/highlighting.rs` | File and syntax-preview HTTP decoding, path resolution, error mapping, and wire projection. |
| `files/` | Transport-independent browsing, file-action preparation and bounded host execution, desktop app discovery, mutations, preview containment and reader stamps, bounded ripgrep search, and highlighter execution. `crate::git` retains Git inspection ownership. |
| `session/launch.rs` | Common launch requests, execution source, destination, launch overrides and reader context. Live sessions own UI intent and reader identity separately from saved agent definitions; startup snapshots them and adoption restores them from tmux metadata. |
| `session/` | Session types, input, validation, wire views. `crate::application` owns daemon events and their shared encoding cache; `api/events.rs` owns event and screen wire conversion. |
| `session/role.rs` | Live agent lifetime and host roles; lifecycle owns retention/cleanup and views projects compatibility flags. |
| `session/validation.rs` | Session name allocation and normalization. |
| `prompt/` | Placeholder scanning and values for prompts and breadcrumbs. |
| `session/manager/` | `Manager` and its guards, configuration synchronization, session lifecycle, capture, task/session integration, workers. |
| `application/projects.rs`, `session/manager/projects.rs` | Application owns project policy and relocation coordination; session supplies guards, live identity references, attachments, and transaction adapters. See [worktree ownership](daemon-worktrees.md). |
| `application/workers.rs`, `session/manager/workers.rs` | Application owns worker request/preflight policy and template construction; session owns guarded allocation, task/session commit, and cancellation cleanup. |
| `application/workspace_identity.rs` | Workspace identity reservation against records and retained caches; session supplies live references and retains operation guards through publication. |
| `application/errands.rs`, `application/stored_state.rs` | Errand configuration/display policy and stored-state administration. Session retains allocation, guarded reset/restore, and live reconciliation. |
| `application/launch.rs`, `session/manager/launch.rs` | Application resolves explicit and saved-library launch policy; session owns guarded execution. API owns explicit request decoding; saved library entries and music use the same resolved launch request. |
| `session/manager/lifecycle/` | Start, stop, adoption, and reconciliation of configured sessions. |
| `session/manager/delivery.rs` | Run-bound delayed prompt submission, readiness, and auto-resume. Callers compose prompts; capture owns queued input execution. |
| `session/manager/capture/` | Terminal readers, input, frames, scrollback, and title capture. |
| `session/manager/host_metadata.rs` | Host-shell record edits, working-directory/process observations, and serialized background tmux polling. See [host tabs](daemon-host-terminals.md). |
| `application/workspace.rs` | Startup-constructed configuration transactions, storage binding, endpoint publication, and worktree mutation gate. Session runtime borrows the settings path and retains operation guards around workspace effects. |
| `application/startup.rs`, `application/maintenance.rs` | Daemon recovery ordering, catalog polling, and combined maintenance scheduling. Session `init.rs` constructs runtime state; session `maintenance.rs` supplies activity, capture-retry, and host-poll deadlines. The `config/` adapter delegates accepted configuration/auth publication to `application/config_authority.rs`; `lifecycle/reconcile.rs` applies configuration to live sessions. `application/catalogs.rs` owns catalog reload state. |
| `clock.rs`, `paths.rs` | Unix-millisecond timestamps, filesystem metadata, and atomic file writes. Latency measurements use their own monotonic clock in `latency.rs`. |
| `storage/workspace.rs`, `storage/document.rs` | Shared ordered-record discovery, preparation and publication, and TOML extension mechanics. Session/project/checkout schemas retain their own validation and nested extension policy. |
| `storage_id.rs` | OS-random opaque storage IDs and syntax checks; each store owns reservation and collision exclusion. |
| `process.rs` | Shared bounded child capture, timeout, kill, and reap mechanics. |
| `emu/`, `tmux/` | Terminal mirror and tmux transport. `tmux/server.rs` owns server startup and readiness; `emu/serialize.rs` owns cell-to-row encoding and `emu/text.rs` owns escape-sequence stripping; `tmux/control.rs` decodes control-mode output before bytes enter the mirror. |
| `sandbox/`, `presets.rs`, `presets/edit.rs` | Sandbox construction, preset snapshots, and serialized catalog mutations. |
| `config/` | Machine settings, assembled configuration view, domain validation and command resolution. `validation.rs` owns static configuration/project/session checks, `library.rs` owns library-item rules, and `patch.rs` converts JSON settings patches. `settings_document.rs` prepares root-only edits; `crate::storage` owns workspace records and recovery. |
| `config/transactions/` | Serialized preparation, recovery, disk commits, cache rollback, and accepted record/catalog indexes. `session/manager/config/` adapts session validation, guarded application publication, endpoint updates, and announcements. |
| `git/` | Git inspection and restricted command execution; see [Git boundary](daemon-git.md). |
| `worktrees/` | Independent worktree records, bounded Git operations, checkout validation/removal policy, and uncommitted directory guards. See [worktree ownership](daemon-worktrees.md). |
| `tasks.rs`, `tasks/`, `grant.rs` | Mailbox model, indexed policy and persistence adapters; `tasks/io.rs` owns blocking startup and serialized I/O with explicit caller leases; scoped authority. |
| `music/` | Playback selection, catalog resolution, recovery, and serialized source transitions. `ncspot.rs` owns managed-player state and bounded IPC; `session/manager/music.rs` adapts host-terminal lifecycle through `HostSessions`. |
| `audio/`, `jukebox.rs` | Decoded playback and station catalog storage. `tmux/ncspot.rs` owns managed-player and applied-volume markers. See [jukebox](mod-jukebox.md). |
| `usage/` | Provider polling laps and quota normalization. Accepts daemon settings and returns a snapshot plus the next delay; application owns the background loop and retained readout. |
| `bin/` | Launcher, installer, and `slopctl` CLI. |
| `bin/slopctl/logs/stream.rs` | Bounded log fan-in and producer/child cleanup. |

See [session state](daemon-session-state.md), [sandbox isolation](sandbox-isolation.md),
[configuration stores](daemon-config-stores.md), [agent templates](daemon-agent-templates.md),
and [workers](daemon-workers.md) for the contracts that edits must preserve.
