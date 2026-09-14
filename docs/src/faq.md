# FAQ

## RimWorld

### What builds are supported? {#supported-builds}

See [Requirements](requirements.md) for Linux and macOS support and the tested
game distribution.

### Can I play normal RimWorld after installing SlopWorld? {#normal-rimworld}

Yes. SlopWorld uses a separate save-data folder (defaults to
`~/.local/share/slopworld/profile`). The mod refuses to patch when the game is started
without the launcher, so enabling it in a regular game has no effect. Back up your
saves before using SlopWorld regardless.

### Do I need DLCs? {#dlcs}

SlopWorld uses only the core game assets. The launcher seeds `ModsConfig.xml` with all
five expansions listed so the game does not complain, but none are required.

## Sandboxing and safety

### Is it safe? {#safety}

No. SlopWorld gives more isolation than running agents unsandboxed on your desktop, but
that is the extent of the guarantee. There is no seccomp filter or disk quota. Project
directories are read-write, so agents can install git hooks or alter configuration.

**Back up your data before using SlopWorld.**

See the [Security model](reference/security.md) reference.

### How do I give an agent full network access? {#host-network}

Set the agent's network to `host` in the agent editor. This shares the host's full
network stack, including local services.

### What are escape warnings? {#escapes}

A non-empty `escapes` field on a preset means that preset exposes a capability that
reaches back toward the host. The warning appears in the agent editor when the preset
is selected.

## Agents and sessions

### How do I add a new agent CLI? {#new-cli}

Create a command preset with the CLI's launch command and a sandbox preset with its
configuration paths. Place the TOML files in `~/.config/slopworld/presets/`. They
appear in Settings > Commands without rebuilding. See
[Configuring agents](guides/configuring-agents.md).

### What happens when I restart an agent? {#restart}

Restart kills the agent process and its sandbox, then starts a fresh process with the
same configuration. Private state is preserved. The `pasta` network namespace is
recreated. The terminal emulator is rebuilt from the new tmux pane.

### What is Reset Storage? {#reset-storage}

Reset moves an agent's per-session private state to a 14-day trash directory and starts
fresh. Use it for damaged or intentionally fresh tool state, not for network problems.

### Can agents talk to each other? {#agent-communication}

Through scoped grants and task mailboxes. A grant lets one agent watch or type into
another agent's terminal. Task mailboxes let agents delegate structured work. See
[Agent collaboration](guides/agent-collaboration.md).

## Interface

### Bare F-keys do nothing in the terminal {#f-keys}

The mod intercepts bare F-keys for navigation (F1-F6) and Ctrl+backquote for the palette.
Shift+F-key forwards the key to the agent. See
[Keyboard shortcuts](reference/keyboard-shortcuts.md).

### How do I copy text from the terminal? {#copy}

Select text with the mouse, then Ctrl+C copies it. When no text is selected, Ctrl+C
sends SIGINT to the agent.

## Performance

### Eco mode {#eco-mode}

Eco mode stops the game simulation while the daemon and agents continue running. Enable it
from Settings under "Game". Eco leaves foreground responsiveness unchanged. Frame pacing
is controlled separately under "Display"; unfocused windows use 15 FPS.

## Contributing

### Where are the developer notes? {#devnotes}

In `notes/`. They are internal; published documentation
under `docs/` is authoritative. See [Contributing](reference/contributing.md).

For setup and installation problems, see [Troubleshooting](reference/troubleshooting.md).
