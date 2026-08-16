# `slopd/src/` - what each file holds

| File | Holds |
| --- | --- |
| `main.rs` | Startup, the retick loop, the token middleware. |
| `api.rs` | Routes and the WebSocket pump. |
| `session.rs` | Session data types, terminal input helpers and config validation. |
| `manager/lifecycle.rs` | `Manager` construction, configuration, clocks, state transitions and session/project/shortcut lifecycle. |
| `manager/capture.rs` | `Manager` terminal input, emulator readers, screen frames and scroll capture. |
| `manager/caps.rs` | `Manager` capability and grant checks. |
| `emu.rs` | `SessionEmu`, an `alacritty_terminal` per session. |
| `tmux.rs` | Async wrapper over the tmux CLI. |
| `sandbox.rs` | The bubblewrap argv, network modes, and pasta wrapper. |
| `presets.rs` | The preset tables: builtin TOML plus the user's. |
| `jukebox.rs` | The station catalog: builtin/user TOML, metadata, and daemon-side URL resolution. |
| `config.rs` | `config.toml` load, save, and seed. |
| `activity.rs` | Persists the fallback file for state ages when the tmux server has no activity options. |
| `usage.rs` | Polls Anthropic and OpenRouter for what is left of each. |
| `audio.rs` | The jukebox's sound, because the game cannot play it. |
| `clipboard.rs` | The host clipboard. |
| `git.rs` | What a working tree has that its last commit does not. |
| `open.rs` | Opening a URL on the host. |
| `bin/slopworld.rs` | The launcher - see [profile](profile.md). |
| `bin/slopctl.rs` | The host/task CLI; `slopctl logs` reads the game file and daemon journal locally. |

`config.toml` is re-read whenever its mtime moves, on a two-second check and
ahead of every mutating call. A file that does not parse is complained about once.

`emu.rs` **answers** what an app asks the terminal - cursor-position reports
(`ESC[6n`), device attributes, mode queries - rather than dropping them the way
`VoidListener` did, which left Ink (and so Claude Code) anchoring its cursor on
the wrong line. It also takes the three things an app says about itself: the OSC
title, a clipboard write and the bell. OSC 52 is store-only (`Osc52::OnlyCopy`),
which is the right way round when the clipboard is the operator's.
