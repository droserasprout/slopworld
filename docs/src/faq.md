# FAQ

## RimWorld

### What builds are supported?

A native Linux build of RimWorld 1.6. GOG is tested by the developer; Steam should
work the same way. Set `RIMWORLD` to the game directory before building.

### Can I play normal RimWorld after installing SlopWorld?

Yes. SlopWorld uses a separate save-data folder (defaults to
`~/.local/share/slopworld/profile`). The mod refuses to patch when the game is started
without the launcher, so enabling it in a regular game has no effect. Back up your
saves before using SlopWorld regardless.

### Do I need DLCs?

SlopWorld uses only the core game assets. The launcher seeds `ModsConfig.xml` with all
five expansions listed so the game does not complain, but none are required.

## Setup

### The launcher cannot find the game

Set `RIMWORLD` to the directory containing `RimWorldLinux` (or `--game /path`). The
default is `~/GOG Games/RimWorld/game`. For GOG installs managed by Heroic, use
`make gogdl-install` to download the native Linux build.

### The mod loads but nothing happens

The mod's refusal dialog means the game was started without the launcher. Run
`slopworld` or `make run` instead of launching `RimWorldLinux` directly. The launcher
writes a profile marker that the mod checks before patching.

### Multiple instances

The launcher holds a profile-keyed file lock. A second launch for the same profile
is refused. A different profile (set with `--profile` or `SLOPWORLD_PROFILE`) may run
beside the first.

## Sandboxing and safety

### Is it safe?

No. SlopWorld gives more isolation than running agents unsandboxed on your desktop, but
that is the extent of the guarantee. There is no seccomp filter or disk quota. Project
directories are read-write, so agents can install git hooks or alter configuration.

**Back up your data before using SlopWorld.**

See the [Security model](reference/security.md) reference.

### How do I give an agent full network access?

Set the agent's network to `host` in the agent editor. This shares the host's full
network stack, including local services.

### What are escape warnings?

A non-empty `escapes` field on a preset means that preset exposes a host capability
such as Docker, D-Bus, X11, the SSH agent, or 1Password. The warning appears in the
agent editor when the preset is selected. It marks a real capability, not cosmetic
caution.

## Agents and sessions

### How do I add a new agent CLI?

Create a command preset with the CLI's launch command and a sandbox preset with its
configuration paths. Place the TOML files in `~/.config/slopworld/presets/`. They
appear in the Settings > Commands page without rebuilding. See
[Configuring agents](guides/configuring-agents.md).

### What happens when I restart an agent?

Restart kills the agent process and its sandbox, then starts a fresh process with the
same configuration. Private state is preserved. The `pasta` network namespace is
recreated. The terminal emulator is rebuilt from the new tmux pane.

### What is Reset Storage?

Reset moves an agent's per-session private state to a 14-day trash directory and starts
fresh. Use it for damaged or intentionally fresh tool state, not for network problems.

### Can agents talk to each other?

Through scoped grants and task mailboxes. A grant lets one agent watch or type into
another agent's terminal. Task mailboxes let agents delegate structured work. See
[Agent collaboration](guides/agent-collaboration.md).

## Interface

### Bare F-keys do nothing in the terminal

The mod intercepts bare F-keys for navigation (F2-F6) and the palette (F1).
Shift+F-key forwards the key to the agent. See
[Keyboard shortcuts](reference/keyboard-shortcuts.md).

### How do I copy text from the terminal?

Select text with the mouse, then Ctrl+C copies it. When no text is selected, Ctrl+C
sends SIGINT to the agent.

## Performance

### Eco mode

Eco mode stops the game simulation and dims the display. The baked menu background
replaces the live map. Enable it from Settings > Appearance or the command palette.

## Contributing

### Where are the developer notes?

In `notes/`, indexed at `notes/index.md`. They are internal; published documentation
under `docs/` is authoritative. See [Contributing](reference/contributing.md).
