# Daemon source map

Paths are relative to `slopd/src/`. Module declarations own the file inventory.

| Area | Responsibility |
| --- | --- |
| `main.rs`, `application/startup.rs` | Service startup and recovery ordering. `cli.rs` owns Clap syntax and help; main dispatches local CLI exits and benchmarks before loading configuration or starting services. |
| `application/` | Feature coordination, accepted configuration, catalogs, and shared services. Session adapters supply identity and operation guards; API owns wire encoding. |
| `application/workspace.rs` | Configuration transactions, endpoint publication, and the worktree mutation gate. |
| `api/` | Routing, authentication admission, HTTP/Protobuf conversion, and WebSocket transport. |
| `session/` | Live identity, lifecycle, input, capture, activity, and client views. The runtime receives focused service handles rather than application containers. |
| `config/`, `config/transactions/`, `storage/` | Configuration validation, accepted records, persistence, and recovery. See [configuration stores](daemon-config-stores.md). |
| `files/`, `git/` | Host file operations and restricted Git inspection. See [file access](daemon-file-access.md) and [Git](daemon-git.md). |
| `worktrees/` | Checkout records, Git effects, and rollback guards. Application coordinates operations; see [worktrees](daemon-worktrees.md). |
| `sandbox/`, `presets.rs`, `presets/` | Sandbox construction, private state, and preset catalogs. See [sandbox isolation](sandbox-isolation.md) and [presets](daemon-presets.md). |
| `agent_templates/`, `prompt/` | Portable template snapshots and prompt substitution. Application owns catalog and launch coordination; see [templates](daemon-agent-templates.md) and [library](daemon-library.md). |
| `tasks.rs`, `tasks/`, `grant.rs` | Task policy/persistence and scoped authority. See [tasks](agent-tasks.md) and [grants](agent-grants.md). |
| `emu/`, `tmux/` | Terminal mirror and tmux transport. See [capture](daemon-terminal-capture.md). |
| `desktop.rs` | Current desktop environment snapshots for sandbox socket mounts and forwarded variables. |
| `music/`, `audio/`, `jukebox.rs` | Playback transitions, audio output, and station storage. See [jukebox](mod-jukebox.md). |
| `usage/`, `title/` | Provider polling and summary generation/cache. Application owns background publication; see [titles](agent-titles.md). |
| `shared/`, `process.rs`, `paths.rs`, `clock.rs` | Shared wire types, bounded child execution, filesystem helpers, and epoch timestamps. Latency uses its own monotonic clock. |
| `bin/` | Launcher, installer, and `slopctl`. `slopctl/cli.rs` owns the command tree, generated help, and task-ID resolution. Clap parses options before task text; task update positionals are always notes, with identity supplied by `--id` or the injected task ID. Command modules own reusable argument schemas and remote execution; the binary handles local help, completions, paths, and logs before loading credentials. `shared/cli_completions.rs` renders shell scripts from each client's command tree without runtime state. |

Start live-session changes with [state](daemon-session-state.md) and
[lifecycle](daemon-session-lifecycle.md). Worker coordination belongs to
[workers](daemon-workers.md); test and coverage guidance to [build commands](build-commands.md).

CLI parsers retain Clap's styled help and diagnostics until the binary writes them.
`shared/cli_styles.rs` owns the common help palette.
Anstream owns terminal detection and `NO_COLOR` handling for stdout and stderr
independently. Completions and machine-readable output use plain writers.
