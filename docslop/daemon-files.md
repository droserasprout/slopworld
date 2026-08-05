# `slopd/src/` - what each file holds

| File | Holds |
| --- | --- |
| `main.rs` | Startup, the retick loop, the token middleware. |
| `api.rs` | Routes and the WebSocket pump. |
| `session.rs` | `Manager`: live table, state classification, control readers. |
| `emu.rs` | `SessionEmu`, an `alacritty_terminal` per session. |
| `tmux.rs` | Async wrapper over the tmux CLI. |
| `sandbox.rs` | The bubblewrap argv. |
| `presets.rs` | The preset tables: builtin TOML plus the user's. |
| `config.rs` | `config.toml` load, save, seed, migration. |
| `usage.rs` | Polls Anthropic and OpenRouter for what is left of each. |
| `audio.rs` | The jukebox's sound, because the game cannot play it. |
| `clipboard.rs` | The host clipboard. |
| `game.rs` | Launching the game, and whether it is up. |
| `open.rs` | Opening a URL on the host. |
| `perf.rs` | Temporary instrumentation for the terminal pipeline. |
| `bin/slopworld.rs` | The launcher - see [profile](profile.md). |

`config.toml` is re-read whenever its mtime moves, on a two-second check and
ahead of every mutating call. A file that does not parse is complained about once.

`emu.rs` **answers** what an app asks the terminal - cursor-position reports
(`ESC[6n`), device attributes, mode queries - rather than dropping them the way
`VoidListener` did, which left Ink (and so Claude Code) anchoring its cursor on
the wrong line. It also takes the three things an app says about itself: the OSC
title, a clipboard write and the bell. OSC 52 is store-only (`Osc52::OnlyCopy`),
which is the right way round when the clipboard is the operator's.

`perf.rs` aggregates rather than logging per event - the paths it measures run
sixty times a second per session - and is global rather than per-session, the
question being where the pipeline's time goes and not which agent spent it.
`SLOPD_PERF` is the reporting window in seconds; `0` turns it off.
