# `slopd/src/` - what each file holds

| File | Holds |
| --- | --- |
| `main.rs` | Startup, the retick loop, the token middleware. |
| `api/mod.rs` | API module façade and shared request helpers. |
| `api/router.rs` | Axum routes and root-only middleware. |
| `api/handlers.rs` | Shared HTTP guards, common helpers, core session/project/preset/grant handlers, and co-located tests. |
| `api/handlers_*.rs` | Focused HTTP boundaries for tasks, config, files, presets, clipboard, system usage, and audio. |
| `api/types.rs` | HTTP and WebSocket wire request types. |
| `api/ws.rs` | WebSocket upgrade, message handling, and frame pump. |
| `session/mod.rs` | Session state/data types and the public façade for session helpers. |
| `session/input.rs` | Ordered terminal input batching and input-item merging. |
| `session/template.rs` | Runtime template variables and expansion. |
| `session/text.rs` | Terminal text cleanup and UTF-8 helpers. |
| `session/title.rs` | Automatic-title settings, prompt composition, and title requests. |
| `session/validation.rs` | Session config, path, mount, and file-action validation helpers. |
| `session/view.rs` | `SessionView` and `ScreenView` wire serialization. |
| `session/ctrl.rs` | `Manager` storage and client/watch guards. |
| `manager/config.rs` | `Manager` construction, configuration synchronization, clocks and activity persistence. |
| `manager/adoption.rs` | Recovery of tmux sessions that outlived config, including worker metadata and activity restoration. |
| `manager/desktop.rs` | Host MIME associations, desktop-file names and launch paths for Files' Open in menu. |
| `manager/errands.rs` | Temporary and host errand session reservation, project allocation, and sandbox cloning. |
| `manager/sessions.rs` | Session config edits, host-terminal persistence, stored state, and session lookup. |
| `manager/session_lifecycle.rs` | Session stop/forget/restart lifecycle; startup preparation and launch live in `manager/start.rs`. |
| `manager/start.rs` | Session target resolution, preflight/argv construction, tmux launch, and reader/state wiring. |
| `manager/session_state.rs` | Session views, state classification, and the manager retick loop. |
| `manager/library.rs` | Projects, library items, file actions and temporary errands. |
| `manager/workers.rs` | Root-only task-owned worker construction and explicit child metadata. |
| `manager/capture.rs` | Capture façade, shared frame metadata, attach helpers, and capture tests. |
| `manager/capture_input.rs` | Ordered terminal keys, mouse input, paste, and resizing. |
| `manager/capture_title.rs` | Automatic-title capture and title-agent reconciliation. |
| `manager/capture_reader.rs` | Emulator reader and control-channel workers. |
| `manager/capture_frame.rs` | Screen-frame rendering, state updates, and broadcasts. |
| `manager/capture_scroll.rs` | Scrollback capture and scroll-state cleanup. |
| `manager/caps.rs` | `Manager` capability and grant checks. |
| `emu.rs` | `SessionEmu`, an `alacritty_terminal` per session. |
| `tmux.rs` | Async wrapper over the tmux CLI. |
| `sandbox/` | The bubblewrap argv, network modes, bind guard and pasta wrapper - see [sandbox-isolation](sandbox-isolation.md). |
| `sandbox/bind/policy.rs` | Effective preset path resolution, private/shared state sources, resolver targets, and the final refused-path guard. |
| `sandbox/bind/mounts.rs` | Ordered bwrap mounts, environment, DNS/pasta wrapping, and resource-limit argv emission. |
| `presets.rs` | The preset tables: builtin TOML plus the user's. |
| `jukebox.rs` | The user station catalog: TOML, metadata, and daemon-side URL resolution. |
| `config.rs` | Effective config lookup, inheritance, expansion, and the public config façade. |
| `config/model.rs` | Config data model, defaults, and field-level deserialization validation. |
| `config/persistence.rs` | `config.toml` load, save, redaction, and seed. |
| `config/validation.rs` | Cross-entry validation after config deserialization. |
| `manifest.rs` | Generated project-root `SLOPWORLD.md`, Git exclusion, templating, and runtime-context rendering. |
| `endpoint.rs` | The `endpoint.toml` descriptor: writes url + token for the mod, rewrites token on live config edits without changing the bound address. |
| `grant.rs` | Scoped agent tokens - see [agent-grants](agent-grants.md). |
| `paths.rs` | Config/data directory resolution. |
| `title.rs` | Prompt summaries - see [agent-titles](agent-titles.md). |
| `tasks.rs` | Durable task mailboxes - see [agent-tasks](agent-tasks.md). |
| `activity.rs` | Persists the fallback file for state ages when the tmux server has no activity options. |
| `usage.rs` | Usage snapshot types, provider polling/scheduling, per-item filtering, and provider-result merging. |
| `usage/parsing.rs` | Anthropic, OpenRouter, and OpenAI response parsing, window families, spending, and reset timestamps. |
| `usage/providers.rs` | Provider credentials, HTTP polling, shared Anthropic caching, and retry handling. |
| `audio/mod.rs` | The public audio handle, command worker, generation control and state events. |
| `audio/station.rs` | Local playlists, URL/file decoding, stream reconnects and ICY metadata. |
| `audio/playback.rs` | Output-device selection, feeder pacing and callback-safe sample rings. |
| `clipboard.rs` | The host clipboard. |
| `git.rs` | What a working tree has that its last commit does not. |
| `open.rs` | Opening a URL on the host. |
| `bin/slopworld.rs` | The launcher and host command entry point - see [profile](profile.md). |
| `bin/slopctl.rs` | `slopctl` entry point and global dispatch; its `slopctl/` siblings split commands, local logs, HTTP, formatting, and tests. |
| `bin/slopctl/commands.rs` | Task/session command parsing, handlers, inbox filtering, and command help. |
| `bin/slopctl/logs.rs` | Local game/journal log selection, subprocesses, following, and rendering inputs. |
| `bin/slopctl/http.rs` | Endpoint loading, authenticated daemon requests, and status snapshots. |
| `bin/slopctl/format.rs` | Human/JSON task, status, and age formatting. |
| `bin/slopctl/tests.rs` | `slopctl` parser, log, HTTP, and command behavior tests. |
| `bin/slopworld/mod_install.rs` | The `slopworld mod` installer; stages `Mods/SlopWorld` and refuses unsafe roots or source overlap. |
| `version.rs` | Pure build-version policy shared by Cargo's build script and its tests. |

`config.toml` is re-read whenever its mtime moves, on a two-second check and
ahead of every mutating call. A file that does not parse is complained about once.

`emu.rs` **answers** what an app asks the terminal - cursor-position reports
(`ESC[6n`), device attributes, mode queries - rather than dropping them the way
`VoidListener` did, which left Ink (and so Claude Code) anchoring its cursor on
the wrong line. It also takes the three things an app says about itself: the OSC
title, a clipboard write and the bell. OSC 52 is store-only (`Osc52::OnlyCopy`),
which is the right way round when the clipboard is the operator's.
