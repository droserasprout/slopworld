# SlopWorld

RimWorld, but colonists are real coding agents ¯\_(ツ)_/¯

Consists of `SlopWorld` mod (C#, Harmony), terminal daemon `slopd` (Rust, tmux, alactitty) communicating via WebSocket, and the `slopworld` launcher.

## Installation

- Install Linux build of RimWorld (tested with GOG release)
- Run `RIMWORLD=<path_to_game> make install`. This will enable and start `slopd` service holding terminal sessions, and put the `slopworld` launcher in `~/.local/bin`.
- Run `slopworld` (or `make run`). It creates a game profile of its own in `~/.local/share/slopworld/profile` - separate saves, with every mod and DLC except this one switched off - and starts the game there. First launch drops you straight into a new colony.
- Add and configure agents.

Do not launch RimWorld directly: the mod refuses to patch anything outside its own profile, and says so. Your normal saves are never touched.

## F.A.Q

### Is it vibecode?

Hell yeah! All except this file and TODO. It would be disrespectful to the workforce to interfere.

### How to play? 😭

Is it a fucking game to you, meatbag?

### What agents are supported?

Currently Claude Code only. See TODO for priorities.
