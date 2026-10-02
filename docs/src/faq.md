# FAQ

## RimWorld and profiles

### Which builds can I use? {#supported-builds}

See [Requirements](requirements.md) for supported platforms and game builds.

### Can I play normal RimWorld after installing SlopWorld? {#normal-rimworld}

Yes. SlopWorld uses a separate save-data profile. If its mod is loaded without
that profile's marker, it reports the missing marker and shows a dialog.
See [Game profiles](guides/game-profiles.md) for keeping the two setups separate.

### Do I need DLCs? {#dlcs}

No. SlopWorld uses only the core game assets.

## Agents and sandboxing

### Is it safe? {#safety}

Isolation does not guarantee security. Agents have read-write project access by
default, though mounts can be configured read-only. Back up your data before use.
See the [Security model](reference/security.md) for limits.

### How do I give an agent full network access? {#host-network}

Set **Network** to `host` in the agent editor. See
[Network configuration](guides/configuring-agents.md#network) for details.

### What are escape warnings? {#escapes}

They identify host capabilities granted by a preset outside the sandbox. See
[Configuring sandboxes](guides/configuring-sandboxes.md).

### How do I add a new agent CLI? {#new-cli}

Add an app preset and a sandbox preset using the editors or TOML configuration.
See [Configuring agents](guides/configuring-agents.md).

### What happens when I restart an agent? {#restart}

Restart starts a new process with the saved settings and the same private state.
See [Backup and recovery](guides/backup-and-recovery.md).

### What does Reset private state do? {#reset-storage}

It stops the agent and moves its private state to trash for at least 14 days.
The next start creates fresh private state. See
[Backup and recovery](guides/backup-and-recovery.md) for recovery and reset steps.

### Can agents talk to each other? {#agent-communication}

Yes. Terminal grants and task mailboxes support collaboration. See
[Agent collaboration](guides/agent-collaboration.md).

## Interface

### Why do function keys not reach the agent? {#f-keys}

The mod reserves function keys for navigation; Shift sends them to the terminal.
See [Keyboard shortcuts](reference/keyboard-shortcuts.md).

### How do I copy text from the terminal? {#copy}

Select text, then press Ctrl+C. Without a selection, Ctrl+C interrupts the process.
See [Keyboard shortcuts](reference/keyboard-shortcuts.md).

### What does Eco mode do? {#eco-mode}

Eco mode stops the game simulation while the daemon and agents continue running.
Frame pacing is a separate control under **Settings > Display**. See
[Interface](tour/interface.md).
