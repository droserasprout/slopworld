# FAQ

## General

### Is it a serious project or a one-shot toy?

Very serious! SlopWorld is a lightweight yet complete IDE that you can extend with your favorite Linux tools.

### Is it vibecoded slop?

Yes and no.

I haven't read every line of code in this repo. Yet I consider SlopWorld a carefully crafted piece of software. Decide for yourself.

### So it's gonna be abandoned soon?

You never know.

But as of v0.0.2, I am dogfooding hard. Haven't opened VS Code or GNOME Terminal for many weeks of active development.

### Is it stable?

No.

Not even `0.1.0`. Expect the unexpected.

### Does the "game" affect IDE functionality?

No.

All the "gameplay" is purely cosmetic and never interferes with your shells, agents, or whatever.

So you can safely cast a fireball at an agent (`2` key). Or kill yourself. In the game.

If you find an easter egg, it's also purely cosmetic.

### Can I use SlopWorld for oldskool coding without AI agents?

You can, but it makes little sense.

## RimWorld

### Do I need to buy RimWorld to run this software?

Yes.

### Where can I buy it?

Anywhere: [Steam](https://store.steampowered.com/app/294100/RimWorld/),
[GOG](https://www.gog.com/en/game/rimworld), or
[directly from Ludeon Studios](https://rimworldgame.com/).

If you want to play the vanilla game, choose Steam bc Workshop and mods are the best part. In other cases, consider DRM-free stores.

### Which game builds can I use? {#supported-builds}

- For x86_64 Linux: native builds.
- For Apple silicon Macs: native builds with a sidecar container (slopd-in-Docker).

RimWorld doesn't have ARM64 builds for Linux.

See [Requirements](../installation/linux.md#requirements) for supported platforms and game builds.

### Can I play vanilla RimWorld after installing SlopWorld? {#normal-rimworld}

You can and you should!

SlopWorld uses a separate save-data profile. If its mod is loaded without
that profile's marker, it shows an error and prevents loading the colony.

SlopWorld stays visible in vanilla's mods screen. Just keep it disabled and ignore it.

### Do I need RimWorld DLCs to run SlopWorld? {#dlcs}

No. SlopWorld uses only the core game assets.

If you play the vanilla game tho, I highly recommend buying DLCs, especially Royalty. They are fun and bring many new mechanics.

### Can I mix SlopWorld with other mods?

No. The launcher ensures that the SlopWorld profile only enables one mod.

## Agents and sandboxing

### Should I feel safe when using SlopWorld sandboxing? {#safety}

Definitely not!

Consider sandboxing a convenience, not a security feature. Like a nudge to agents: "please don't touch my shit".

See the [Security model](../sandbox/security.md) for limits.

### OK, I feel unsafe now, thanks...

To get some peace of mind using SlopWorld, try this:

- Use narrow read-only mounts.
- Carefully read the agent preview.
- Back up your projects and other data.

### How do I configure an agent's network access? {#host-network}

You can choose from `host`, `private` and `none` in the agent editor. See [Network configuration](../agents/configuring-agents.md#network) for details.

Granular access with a DNS proxy is not available yet, but it's planned.

### What are the yellow escape warnings on sandbox presets? {#escapes}

Read those warnings carefully before enabling a preset for an agent!

They identify host capabilities granted by a preset outside the sandbox. See
[Sandbox presets](../sandbox/sandbox-presets.md).

For example, the "systemd" preset gives agents access to the filesystem outside the sandbox via "systemd-run".

### What agents, shells, and pagers are supported? {#new-cli}

You can use any tools you like with SlopWorld.

"Supported" status means the tool was somewhat tested by me, has an app/sandbox preset with defaults, and probably has some other integrations.

You can add or override app or sandbox presets from the game UI or put TOML definitions directly in SlopWorld's config directory.
See [Configuring agents](../agents/configuring-agents.md).

### What happens when I restart an agent? Reinstall the game or the daemon? {#restart}

Closing the game or restarting the daemon preserves running tmux sessions. Rebooting the host or restarting/replacing the sidecar ends them.

See [Session lifecycle](../agents/session-lifecycle.md) for restart behavior and
what survives closing the game or restarting the daemon.

### What does Reset private state do? {#reset-storage}

See [Session lifecycle](../agents/session-lifecycle.md#private-state-and-recovery) for
what is reset and [Backup and recovery](../maintenance/backup-and-recovery.md) for recovery steps.

### Can agents communicate with each other? {#agent-communication}

Yes. Terminal grants and task mailboxes support collaboration. See
[Agent collaboration](../agents/agent-collaboration.md).

## Interface

### How do I pass function keys to the terminal session? {#f-keys}

The mod reserves function keys for navigation. Pressing them with Shift sends them to the terminal.
See [Keyboard shortcuts](../reference/keyboard-shortcuts.md).

### How do I copy text from the terminal? {#copy}

You can use the standard Ctrl+Shift+C/V shortcuts or the context menu.

When text is selected, you can also use Ctrl+C/V directly. Without a selection, Ctrl+C interrupts the process.

See [Keyboard shortcuts](../reference/keyboard-shortcuts.md).

### I have created an agent template, but agents can't spawn it as a worker

Go to **Settings > Agents > Workers** to configure the templates available to agents.

This is a precaution to prevent agents from escalating privileges with less sandboxed workers spawned from templates.

### How to send F-key or Alt combo to the terminal process bypassing SlopWorld shortcuts?

Use Shift as modifier.

e.g, to send F1 press Shift+F1. To send Alt+A press Alt+Shift+A. Sending actual Shift+F1 and Alt+Shift+A is not possible yet.

## Fun

### SlopWorld has an internet radio feature, but I don't see any stations in the Jukebox

I would love to include presets for cool stations. But many of them explicitly ask not to include stream URLs in games, on websites, etc.

You gotta add them manually from "Settings > Audio" or by putting TOML in `~/.config/slopworld/jukebox`.

I can recommend my favorite eclectic stations for a good coding sesh:

- Radio Paradise
- WeFunk Radio
- WFMU Freeform Radio
- Kiosk Radio
- dublab
- NTS Radio 1
- KEXP

Find stream URLs and add them to SlopWorld. Then find a donation link on each station's website and do the right thing.

### How was the soundtrack made?

100% human shit by Terry Fail (that's also me).

- [Strudel](https://strudel.cc/) for livecoding
- [Bitwig Studio](https://www.bitwig.com/overview/) for mastering

Both are awesome software I recommend.

I plan to open-source my soundtrack's Strudel code one day.

By the way, I maintain a [Strudel fork](https://codeberg.org/droserasprout/strudel) with UI/UX fixes and advanced exporting capabilities; check this out.

## Misc

### What about the privacy policy?

SlopWorld does not send telemetry data to anyone and never will.

SlopWorld uses network access for these features, directly or indirectly:

- Polling provider APIs for usage
- Internet radio
- Online recognition via songrec
- Spotify via ncspot

I am considering adding a button to show a simple feedback form that sends one plaintext POST with your message to my server.

### I just want an IDE, without the gameplay

Enable "Eco mode" in "Settings > General".

It stops the game simulation and replaces the main screen with a static animation.

You'll get huge CPU savings, but RAM consumption stays roughly the same.

See [Eco mode](../workspace/gameplay.md#eco-mode) for details.

### I am deeply offended by this "game"!

Enable "Granny's Visiting" in "Settings > General".
