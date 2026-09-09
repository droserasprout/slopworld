# Overview

RimWorld with the colony sim replaced by live AI coding agents. One tmux session
on the host is one colonist; select a colonist to type at the agent.

- `slopd/` - Rust daemon. Owns tmux, the sandbox, the terminal emulator and
  `config.toml`. systemd user service. Ships the `slopworld` launcher.
- `mod/` - C# RimWorld mod (Harmony, **1.6 only**).

HTTP + WebSocket on `127.0.0.1:7717`. The daemon is the source of truth; the mod
mirrors it and keeps no session state.

Start with the focused note for the area you are changing.
