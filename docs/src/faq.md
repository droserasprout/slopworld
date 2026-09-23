# FAQ

## RimWorld

### Which builds can I use? {#supported-builds}

See [Requirements](requirements.md) for Linux and macOS support and the tested
game distribution.

### Can I play normal RimWorld after installing SlopWorld? {#normal-rimworld}

Yes. SlopWorld uses a separate folder for save data. The default folder is
`~/.local/share/slopworld/profile`. The mod does not patch RimWorld when you start it
without the launcher.

Back up your saves before you use SlopWorld. See
[Backup and recovery](guides/backup-and-recovery.md).

### Do I need DLCs? {#dlcs}

SlopWorld uses only the core game assets. The launcher adds all five expansions to
`ModsConfig.xml` to prevent game warnings. SlopWorld does not need these expansions.

## Sandboxing and safety

### Is it safe? {#safety}

No. SlopWorld gives agents more isolation than an unsandboxed desktop process, but it does not
guarantee security. It has no general seccomp policy or disk quota.
Agents can read and write project directories. They can install Git hooks or change repository
configuration in those directories.

**Back up your data before you use SlopWorld.**

See the [Security model](reference/security.md) reference.

### How do I give an agent full network access? {#host-network}

In the agent editor, set **Network** to `host`.
The agent then uses the host network and can reach local services.

### What are escape warnings? {#escapes}

If a preset has a non-empty `escapes` field, it identifies a host capability outside the sandbox.
The agent editor shows a warning when you select that preset.

## Agents and sessions

### How do I add a new agent CLI? {#new-cli}

Create an app preset with `slopctl preset create`.
Create a sandbox preset with the CLI's configuration paths.
Save the TOML files in `~/.config/slopworld/app_presets/` and
`~/.config/slopworld/sandbox_presets/`.
The app appears in **Settings > Commands > Apps**. The sandbox appears in **Settings > Sandbox**.
You do not need a new build. See
[Configuring agents](guides/configuring-agents.md).

### What happens when I restart an agent? {#restart}

Restart stops the agent process and its sandbox.
It starts a new process with the saved agent settings and the same private state.
The daemon creates a new `pasta` namespace when the agent uses private networking.
The daemon rebuilds the terminal emulator from the tmux pane.

### What does Reset private state do? {#reset-storage}

Reset stops the agent and moves its private state to trash for 14 days.
The next start creates new private state and copies host files and configured seeds.

Use Reset private state to clear damaged or unwanted state.
Do not use it to fix network problems.

### Can agents talk to each other? {#agent-communication}

Yes. Agents use grants with limited permissions and task mailboxes.
A grant lets one agent read or enter text in another agent's terminal.
Task mailboxes let agents assign structured work to other agents. See
[Agent collaboration](guides/agent-collaboration.md).

## Interface

### Why do function keys not reach the agent? {#f-keys}

The mod uses F1-F6 for navigation and Ctrl+backquote for the command palette.
To send a function key to the agent, press Shift with that function key. See
[Keyboard shortcuts](reference/keyboard-shortcuts.md).

### How do I copy text from the terminal? {#copy}

Select text with the mouse. Then press Ctrl+C to copy the text.
If you have not selected text, Ctrl+C sends SIGINT to the agent.

## Performance

### Eco mode {#eco-mode}

Eco mode stops the game simulation. The daemon and agents continue to operate.
Eco mode does not change frame pacing.
The **Settings > Appearance > Interface** page controls frame pacing.
Unfocused windows use 15 FPS.

To enable Eco mode, open **Settings > General**. Under **Game**, select **Eco mode**.

## Contributing

### Where are the developer notes? {#devnotes}

The developer notes are in `notes/`. They are for internal use.
If they disagree with the published documentation, follow `docs/`.
See [Contributing](reference/contributing.md).

For setup and installation problems, see [Troubleshooting](reference/troubleshooting.md).
