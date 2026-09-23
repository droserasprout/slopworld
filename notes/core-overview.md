# Overview

SlopWorld replaces the RimWorld colony simulation with AI coding agents.
Each tmux session on the host corresponds to one colonist.

- `slopd/` - Rust daemon. It controls tmux, the sandbox, the terminal emulator, and
  `config.toml`. It operates as a systemd user service. It includes the `slopworld` launcher.
- `mod/` - C# RimWorld mod (Harmony, **1.6 only**).

The mod connects to the daemon through HTTP and WebSocket at `127.0.0.1:7717`.
The daemon controls the session state. The mod shows this state and does not keep separate session state.

Select a colonist to enter text in the agent terminal.
Before you change an area, read the note for that area.
