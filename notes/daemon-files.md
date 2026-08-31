# `slopd/src/` - what each file holds

| File | Holds |
| --- | --- |
| `main.rs` | Startup, the retick loop, the token middleware. |
| `api/mod.rs` | API module façade and shared request helpers. |
| `api/router.rs` | Axum routes and root-only middleware. |
| `api/handlers.rs` | HTTP handlers and their co-located tests. |
| `api/types.rs` | HTTP and WebSocket wire request types. |
| `api/ws.rs` | WebSocket upgrade, message handling, and frame pump. |
| `session/mod.rs` | Session state/data types, terminal input helpers and config validation façade. |
| `session/view.rs` | `SessionView` and `ScreenView` wire serialization. |
| `session/ctrl.rs` | `Manager` storage and client/watch guards. |
| `manager/config.rs` | `Manager` construction, configuration synchronization, clocks and activity persistence. |
| `manager/desktop.rs` | Host MIME associations and desktop-file display names for Files' Open in menu. |
| `manager/sessions.rs` | Session targets, lifecycle, stored state, state classification and views. |
| `manager/library.rs` | Projects, library items, file actions and temporary errands. |
| `manager/workers.rs` | Root-only task-owned worker construction and explicit child metadata. |
| `manager/capture.rs` | `Manager` terminal input, emulator readers, screen frames and scroll capture. |
| `manager/caps.rs` | `Manager` capability and grant checks. |
| `emu.rs` | `SessionEmu`, an `alacritty_terminal` per session. |
| `tmux.rs` | Async wrapper over the tmux CLI. |
| `sandbox/` | The bubblewrap argv, network modes, bind guard and pasta wrapper - see [sandbox-isolation](sandbox-isolation.md). |
| `presets.rs` | The preset tables: builtin TOML plus the user's. |
| `jukebox.rs` | The user station catalog: TOML, metadata, and daemon-side URL resolution. |
| `config.rs` | `config.toml` load, save, and seed. |
| `manifest.rs` | Generated project-root `SLOPWORLD.md`, Git exclusion, templating, and runtime-context rendering. |
| `endpoint.rs` | The `endpoint.toml` descriptor: writes url + token for the mod, rewrites token on live config edits without changing the bound address. |
| `grant.rs` | Scoped agent tokens - see [agent-grants](agent-grants.md). |
| `paths.rs` | Config/data directory resolution. |
| `title.rs` | Prompt summaries - see [agent-titles](agent-titles.md). |
| `tasks.rs` | Durable task mailboxes - see [agent-tasks](agent-tasks.md). |
| `activity.rs` | Persists the fallback file for state ages when the tmux server has no activity options. |
| `usage.rs` | Polls Anthropic, OpenRouter and OpenAI for what is left of each. |
| `audio/mod.rs` | The public audio handle, command worker, generation control and state events. |
| `audio/station.rs` | Local playlists, URL/file decoding, stream reconnects and ICY metadata. |
| `audio/playback.rs` | Output-device selection, feeder pacing and callback-safe sample rings. |
| `clipboard.rs` | The host clipboard. |
| `git.rs` | What a working tree has that its last commit does not. |
| `open.rs` | Opening a URL on the host. |
| `bin/slopworld.rs` | The launcher - see [profile](profile.md). |
| `bin/slopctl.rs` | The host/task CLI; `slopctl logs` reads the game file and daemon journal locally. |
| `bin/slopmod.rs` | The tested host-side mod installer; stages `Mods/SlopWorld` and refuses unsafe roots or source overlap. |
| `version.rs` | Pure build-version policy shared by Cargo's build script and its tests. |

`config.toml` is re-read whenever its mtime moves, on a two-second check and
ahead of every mutating call. A file that does not parse is complained about once.

`emu.rs` **answers** what an app asks the terminal - cursor-position reports
(`ESC[6n`), device attributes, mode queries - rather than dropping them the way
`VoidListener` did, which left Ink (and so Claude Code) anchoring its cursor on
the wrong line. It also takes the three things an app says about itself: the OSC
title, a clipboard write and the bell. OSC 52 is store-only (`Osc52::OnlyCopy`),
which is the right way round when the clipboard is the operator's.
