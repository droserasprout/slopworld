<!-- Generated from README.md by just refresh-introduction. Do not edit. -->

<p align="center">
  <img src="images/slopworld.png" alt="SlopWorld logo" width="64" height="64">
</p>

<h1 align="center">SlopWorld</h1>

<img src="images/screenshot.png" alt="SlopWorld workspace in RimWorld" width="320" align="right">

SlopWorld is a fun terminal-focused IDE for agentic coding, built on RimWorld.
Your agents' sessions appear as colonists. Select one to open its terminal and give
it work.

- Run [agent CLIs](agents/configuring-agents.md), [shells](terminals/host-shells.md), and tools in [persistent terminal sessions](terminals/terminal.md).
- Manage [projects](workspace/configuring-projects.md), [mounts](workspace/project-mounts.md), and [Git worktrees](workspace/project-worktrees.md) for parallel work.
- Configure [sandboxing](sandbox/sandboxing.md) and [network access](agents/configuring-agents.md#network) per agent.
- [Browse files](workspace/files-and-search.md#browse-and-open-files), [search code](workspace/files-and-search.md#search-workspace-text), [review diffs](workspace/review-changes.md#change-review), and [commit changes](workspace/review-changes.md#staging-and-commits).
- [Hand off tasks](agents/agent-collaboration.md#worker-delegation) between agents and [track completion](agents/agent-collaboration.md#task-mailboxes).
- Customize [fonts, colors](customization/settings.md), and the [UI layout](workspace/interface.md).
- Enjoy the [original soundtrack or tune to internet radio](customization/jukebox.md).

## What's inside

<img src="images/introduction-terminal.png" alt="Codex session in SlopWorld with the terminal context menu open" width="240" align="right">

Your favorite Linux tooling, well-integrated:

- **Alacritty** for [terminal emulation](terminals/terminal-interface.md).
- **tmux** for [multiplexing and persistent sessions](terminals/terminal.md).
- **Bubblewrap** for [sandboxing and controlling filesystem access](sandbox/sandboxing.md).
- **pasta** for [networking in private sandboxes](agents/configuring-agents.md#network).
- **git** for [version control](workspace/review-changes.md) and [worktrees](workspace/project-worktrees.md).
- Small UNIX friends: [pagers, highlighters](customization/settings.md#applying-changes), [ripgrep](workspace/files-and-search.md#search-workspace-text), [git-delta](workspace/review-changes.md#change-review).

SlopWorld consists of three components:

- A Rust daemon, [`slopd`](development/architecture.md), manages the sessions and sandboxes.
- The RimWorld mod, `io.drsr.slopworld`, puts their terminals and status into the [game interface](workspace/interface.md).
- [`slopctl`](reference/slopctl.md) provides a command-line interface to the [daemon's API](reference/api.md).

## Get started

<img src="images/introduction-work.png" alt="SlopWorld terminal showing a code diff beneath a tooltip about automated work" width="240" align="right">

You need a native build of **RimWorld 1.6**, preferably the latest one. Buy the game
on [Steam](https://store.steampowered.com/app/294100/RimWorld/),
[GOG](https://www.gog.com/en/game/rimworld), or
[directly from Ludeon Studios](https://rimworldgame.com/).

For Linux host, see the [requirements](installation/linux.md#requirements) and
[installation guide](installation/linux.md).

For experimental sidecar mode (`slopd` in Docker) follow the [macOS](installation/macos.md)
and [sidecar worker](installation/sidecar.md) guides.

### From binaries

Binary packages for Arch and deb-based distros are published to [GitHub Releases](https://github.com/droserasprout/slopworld/releases).

See the [installation instructions](installation/linux.md)
for installing a package, attaching the mod, and starting the daemon.

### From source

<img src="images/introduction-sidebar.png" alt="SlopWorld project and agent sidebar with the session context menu open" width="160" align="right">

Install the [`just`](https://github.com/casey/just#installation) command runner
and the other [build prerequisites](development/build.md#prerequisites).

```sh
git clone https://github.com/droserasprout/slopworld.git
cd slopworld
RIMWORLD=/path/to/RimWorld/game just install
slopworld
```

`RIMWORLD` defaults to `~/GOG Games/RimWorld/game`.

Always start the "game" with `slopworld`. The mod only activates in a separate profile and should stay disabled in vanilla installation.

## License

<img src="images/introduction-footer.png" alt="SlopWorld agents gathered in a RimWorld landscape filled with glowing lights" width="240" align="right">

SlopWorld is licensed under [MIT](https://github.com/droserasprout/slopworld/blob/main/LICENSE). Bundled third-party components
and assets retain their own [licenses and attribution](https://github.com/droserasprout/slopworld/blob/main/licenses/README.md).

> “Portions of the materials used to create this content/mod are trademarks and/or copyrighted works of Ludeon Studios Inc. All rights reserved by Ludeon. This content/mod is not official and is not endorsed by Ludeon.”
