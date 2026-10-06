<p align="center">
  <img src="images/slopworld.png" alt="SlopWorld logo" width="128" height="128">
</p>

<h1 align="center">SlopWorld</h1>

<p align="center">
  <img src="images/screenshot.png" alt="SlopWorld workspace in RimWorld" width="960">
</p>

SlopWorld is a fun terminal-focused IDE for agentic coding, built on RimWorld.
Your agents' sessions appear as colonists. Select one to open its terminal and give
it work.

- Run agent CLIs and shells, group them by project, and switch between terminals.
- Manage projects, mounts, and Git worktrees for parallel work.
- Browse files, search code, review diffs, and commit changes.
- Hand off tasks between agents.
- Customize fonts, colors, and the UI layout.

## What's inside

Your favorite Linux tooling, well-integrated:

- **tmux** for terminal multiplexing and persistent sessions.
- **Bubblewrap** for sandboxing and controlling filesystem access.
- **passt**, via **pasta**, for networking in private sandboxes.
- **Git** for version control and worktrees.
- Small UNIX friends: pagers, highlighters, ripgrep, git-delta.

SlopWorld consists of three components:

- A Rust daemon, `slopd`, manages the sessions and sandboxes.
- The RimWorld mod, `io.drsr.slopworld`, puts their terminals and status into the game interface.
- `slopctl` provides a command-line interface to the daemon's API.

## Get started

You need a native build of **RimWorld 1.6**, preferably the latest one. Buy the game
on [Steam](https://store.steampowered.com/app/294100/RimWorld/),
[GOG](https://www.gog.com/en/game/rimworld), or
[directly from Ludeon Studios](https://rimworldgame.com/).

See the [requirements](requirements.md) and
[installation guide](install.md), or follow the [macOS](guides/macos.md)
and [sidecar worker](guides/sidecar.md) guides.

### From binaries

See the [Debian and Ubuntu instructions](install.md#debian-and-ubuntu)
for installing a package, attaching the mod, and starting the daemon.

### From source

Install the [`just`](https://github.com/casey/just#installation) command runner
and the other [build prerequisites](build.md#prerequisites).

```sh
git clone https://github.com/droserasprout/slopworld.git
cd slopworld
RIMWORLD=/path/to/RimWorld/game just install
slopworld
```

`RIMWORLD` defaults to `~/GOG Games/RimWorld/game`. If you bought the game from
GOG, run `just gogdl-login gogdl-install` to install it automatically.

Always start the game with `slopworld`. The mod requires a SlopWorld game profile.

## License

SlopWorld is licensed under [MIT](https://github.com/droserasprout/slopworld/blob/main/LICENSE). Bundled third-party components
and assets retain their own [licenses and attribution](https://github.com/droserasprout/slopworld/blob/main/licenses/README.md).

> “Portions of the materials used to create this content/mod are trademarks and/or copyrighted works of Ludeon Studios Inc. All rights reserved by Ludeon. This content/mod is not official and is not endorsed by Ludeon.”
