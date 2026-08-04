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
| `clipboard.rs` | The host clipboard. |
| `game.rs` | Launching the game, and whether it is up. |
| `open.rs` | Opening a URL on the host. |
| `bin/slopworld.rs` | The launcher - see [profile](profile.md). |

`config.toml` is re-read whenever its mtime moves, on a two-second check and
ahead of every mutating call. A file that does not parse is complained about once.
