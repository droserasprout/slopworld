# SlopWorld

RimWorld, but colonists are real coding agents ¯\_(ツ)_/¯

Consists of `SlopWorld` mod (C#, Harmony) and terminal daemon `slopd` (Rust, tmux, alactitty) communicating via WebSocket.

## Installation

- Install Linux build of RimWorld (tested with GOG release)
- Run `RIMWORLD=<path_to_game> make install`. This will enable and start `slopd` service holding terminal sessions.
- In game, enable SlopWorld mod, disable other mods and DLCs.
- Create new colony.
- Add and configure agents.

## F.A.Q

### Is it vibecode?

Hell yeah! All except this file and TODO. It would be disrespectful to the workforce to interfere.

### How to play? 😭

Is it a fucking game to you, meatbag?

### What agents are supported?

Currently Claude Code only. See TODO for priorities.
