# FAQ

## RimWorld and profiles

### Which builds can I use? {#supported-builds}

See [Requirements](../getting-started/install.md#requirements) for supported platforms and game builds.

### Can I play normal RimWorld after installing SlopWorld? {#normal-rimworld}

Yes. SlopWorld uses a separate save-data profile. If its mod is loaded without
that profile's marker, it reports the missing marker and shows a dialog.
See [Game profiles](../maintenance/game-profiles.md) for keeping the two setups separate.

### Do I need DLCs? {#dlcs}

No. SlopWorld uses only the core game assets.

## Agents and sandboxing

### Is it safe? {#safety}

Isolation does not guarantee security. Agents have read-write project access by
default, though mounts can be configured read-only. Back up your data before use.
See the [Security model](../sandbox/security.md) for limits.

### How do I give an agent full network access? {#host-network}

Set **Network** to `host` in the agent editor. See
[Network configuration](../agents/configuring-agents.md#network) for details.

### What are escape warnings? {#escapes}

They identify host capabilities granted by a preset outside the sandbox. See
[Sandbox presets](../sandbox/sandbox-presets.md).

### How do I add a new agent CLI? {#new-cli}

Add an app preset and a sandbox preset using the editors or TOML configuration.
See [Configuring agents](../agents/configuring-agents.md).

### What happens when I restart an agent? {#restart}

See [Session lifecycle](../agents/session-lifecycle.md) for restart behavior and
what survives closing the game or restarting the daemon.

### What does Reset private state do? {#reset-storage}

See [Session lifecycle](../agents/session-lifecycle.md#private-state-and-recovery) for
what is reset and [Backup and recovery](../maintenance/backup-and-recovery.md) for recovery steps.

### Can agents talk to each other? {#agent-communication}

Yes. Terminal grants and task mailboxes support collaboration. See
[Agent collaboration](../agents/agent-collaboration.md).

## Interface

### Why do function keys not reach the agent? {#f-keys}

The mod reserves function keys for navigation; Shift sends them to the terminal.
See [Keyboard shortcuts](../reference/keyboard-shortcuts.md).

### How do I copy text from the terminal? {#copy}

Select text, then press Ctrl+C. Without a selection, Ctrl+C interrupts the process.
See [Keyboard shortcuts](../reference/keyboard-shortcuts.md).

### What does Eco mode do? {#eco-mode}

See [Eco mode](../getting-started/gameplay.md#eco-mode) for simulation, terminal, and saving behavior.
