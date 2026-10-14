<p align="center">
  <img src="docs/src/images/slopworld.png" alt="SlopWorld logo" width="64" height="64">
</p>

<h1 align="center">SlopWorld</h1>

<img src="docs/src/images/screenshot.jpg" alt="SlopWorld workspace in RimWorld" width="320" align="right">

SlopWorld is a fun terminal-focused IDE for agentic coding, built on RimWorld.
Your agents' sessions appear as colonists. Select one to open its terminal and give
it work.

- Run [agent CLIs](https://slopworld.space/agents/configuring-agents.html), [shells](https://slopworld.space/terminals/host-shells.html), and tools in [persistent terminal sessions](https://slopworld.space/terminals/terminal.html).
- Manage [projects](https://slopworld.space/workspace/configuring-projects.html), [mounts](https://slopworld.space/workspace/project-mounts.html), and [Git worktrees](https://slopworld.space/workspace/project-worktrees.html) for parallel work.
- Configure [sandboxing](https://slopworld.space/sandbox/sandboxing.html) and [network access](https://slopworld.space/agents/configuring-agents.html#network) per agent.
- [Browse files](https://slopworld.space/workspace/files-and-search.html#browse-and-open-files), [search code](https://slopworld.space/workspace/files-and-search.html#search-workspace-text), [review diffs](https://slopworld.space/workspace/review-changes.html#change-review), and [commit changes](https://slopworld.space/workspace/review-changes.html#staging-and-commits).
- [Hand off tasks](https://slopworld.space/agents/agent-collaboration.html#worker-delegation) between agents and [track completion](https://slopworld.space/agents/agent-collaboration.html#task-mailboxes).
- Customize [fonts, colors](https://slopworld.space/customization/settings.html), and the [UI layout](https://slopworld.space/workspace/interface.html).
- Enjoy the [original soundtrack or tune to internet radio](https://slopworld.space/customization/jukebox.html).

## What's inside

<img src="docs/src/images/introduction-terminal.jpg" alt="Codex session in SlopWorld with the terminal context menu open" width="240" align="right">

Your favorite Linux tooling, well-integrated:

- **Alacritty** for [terminal emulation](https://slopworld.space/terminals/terminal-interface.html).
- **tmux** for [multiplexing and persistent sessions](https://slopworld.space/terminals/terminal.html).
- **Bubblewrap** for [sandboxing and controlling filesystem access](https://slopworld.space/sandbox/sandboxing.html).
- **pasta** for [networking in private sandboxes](https://slopworld.space/agents/configuring-agents.html#network).
- **git** for [version control](https://slopworld.space/workspace/review-changes.html) and [worktrees](https://slopworld.space/workspace/project-worktrees.html).
- Small UNIX friends: [pagers, highlighters](https://slopworld.space/customization/settings.html#applying-changes), [ripgrep](https://slopworld.space/workspace/files-and-search.html#search-workspace-text), [git-delta](https://slopworld.space/workspace/review-changes.html#change-review).

SlopWorld consists of three components:

- A Rust daemon, [`slopd`](https://slopworld.space/development/architecture.html), manages the sessions and sandboxes.
- The RimWorld mod, `io.drsr.slopworld`, puts their terminals and status into the [game interface](https://slopworld.space/workspace/interface.html).
- [`slopctl`](https://slopworld.space/reference/slopctl.html) provides a command-line interface to the [daemon's API](https://slopworld.space/reference/api.html).

## Get started

<img src="docs/src/images/introduction-work.jpg" alt="SlopWorld terminal showing a code diff beneath a tooltip about automated work" width="240" align="right">

You need a native build of **RimWorld 1.6**, preferably the latest one. Buy the game
on [Steam](https://store.steampowered.com/app/294100/RimWorld/),
[GOG](https://www.gog.com/en/game/rimworld), or
[directly from Ludeon Studios](https://rimworldgame.com/).

For Linux host, see the [requirements](https://slopworld.space/installation/linux.html#requirements) and
[installation guide](https://slopworld.space/installation/linux.html).

For experimental sidecar mode (`slopd` in Docker) follow the [macOS](https://slopworld.space/installation/macos.html)
and [sidecar worker](https://slopworld.space/installation/sidecar.html) guides.

### From binaries

Get Arch, Debian/Ubuntu, and Fedora packages from [GitHub Releases](https://github.com/droserasprout/slopworld/releases).

See the [installation instructions](https://slopworld.space/installation/linux.html)
for installing a package, attaching the mod, and starting the daemon.

### From source

<img src="docs/src/images/introduction-sidebar.jpg" alt="SlopWorld project and agent sidebar with the session context menu open" width="160" align="right">

Install the [`just`](https://github.com/casey/just#installation) command runner
and the other [build prerequisites](https://slopworld.space/development/build.html#prerequisites).

```sh
git clone https://github.com/droserasprout/slopworld.git
cd slopworld
RIMWORLD=/path/to/RimWorld/game just install
slopworld
```

`RIMWORLD` defaults to `~/GOG Games/RimWorld/game`.

Always start the "game" with `slopworld`. The mod only activates in a separate profile and should stay disabled in vanilla installation.

## License

<img src="docs/src/images/introduction-footer.jpg" alt="SlopWorld agents gathered in a RimWorld landscape filled with glowing lights" width="240" align="right">

SlopWorld is licensed under [MIT](LICENSE). Bundled third-party components
and assets retain their own [licenses and attribution](licenses/README.md).

> “Portions of the materials used to create this content/mod are trademarks and/or copyrighted works of Ludeon Studios Inc. All rights reserved by Ludeon. This content/mod is not official and is not endorsed by Ludeon.”
