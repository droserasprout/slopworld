# Devnote index

The notes stay flat; this index groups them by how a reader uses them. Keep one
short subject per note and remove stale entries.

## Orientation and policy

- [overview](overview.md) - what this is, the two halves, the wire.
- [house-rules](house-rules.md) - where commits land, and what a note here is for.
- [prose-guide](prose-guide.md) - what belongs in comments and devnotes.
- [human-docs](human-docs.md) - `docs/`: page ownership and source material.
- [attribution](attribution.md) - credits, shipped third-party assets and notice gaps.
- [compatibility](compatibility.md) - pre-0.0.1 wire and path changes are allowed.
- [macOS compatibility](macos-compatibility.md) - native boundaries and the one-container Linux sidecar.
- [macOS compatibility status](macos-compatibility-status.md) - implementation order and verified sidecar support.

## Build, operation, and recovery

- [build-commands](build-commands.md) - Makefile targets and build/install workflows.
- [build-tools](build-tools.md) - formatting, linting, reports, and auxiliary tools.
- [diagnostics](diagnostics.md) - logs, runtime checks, and daemon restart behavior.
- [paths](paths.md) - where config, profile, logs and the tmux socket live.
- [profile](profile.md) - the save-data folder, the launcher, the refusal gates.
- [cpu-optimization](cpu-optimization.md) - C# hot-path reductions.
- [debug-from-sandbox](debug-from-sandbox.md) - which daemon readings are sandbox artifacts, and how to get host truth.
- [known-limitations](known-limitations.md) - network handover hangs and their safe recovery.
- [host-terminals](host-terminals.md) - durable host tabs, tmux metadata and cwd recovery.
- [zsh-terminal](zsh-terminal.md) - zsh key bindings and host/sandbox shell defaults.
- [gotchas](gotchas.md) - the traps that cost a day each.

## Daemon, agents, and sandbox

- [config-stores](config-stores.md) - the daemon's file, the mod's file, the seams.
- [daemon-files](daemon-files.md) - what each `slopd/src/*.rs` holds.
- [daemon-projects](daemon-projects.md) - projects and sessions in `config.toml`.
- [daemon-session-state](daemon-session-state.md) - state classification, clocks, the emulator.
- [daemon-presets](daemon-presets.md) - the preset tables and the sandbox argv.
- [daemon-redeploy](daemon-redeploy.md) - surviving a daemon restart; tmux traps.
- [daemon-usage](daemon-usage.md) - Anthropic, OpenRouter and OpenAI quota polling.
- [daemon-library](daemon-library.md) - library items, ephemeral agents, delivery.
- [wire-protocol](wire-protocol.md) - WS events, client messages, HTTP routes.
- [agent-grants](agent-grants.md) - scoped tokens: one agent watching another, host never.
- [agent-tasks](agent-tasks.md) - durable task mailboxes and the `slopctl` delegation CLI.
- [daemon-workers](daemon-workers.md) - root-owned child workers, task identity, and lifecycle.
- [agent-task-discovery](agent-task-discovery.md) - prompt discovery and safe task-arrival notices.
- [agent-titles](agent-titles.md) - prompt summaries: never/once/always and conversation boundaries.
- [agent-auto-resume](agent-auto-resume.md) - per-agent startup resume and input ordering.
- [sandbox-isolation](sandbox-isolation.md) - the bind guard, private state, marked ways out.
- [sandbox-blast-radius](sandbox-blast-radius.md) - what an agent can and cannot delete of its own `$HOME`.
- [unreached-surfaces](unreached-surfaces.md) - audit of routes and tables with no in-repo caller.

## Mod, simulation, and UI

- [mod-client](mod-client.md) - `Client/`: hub, socket, JSON, config mirror.
- [csharp-tests](csharp-tests.md) - the C# test boundary and pure-logic test project.
- [mod-sim](mod-sim.md) - `Sim/`: colony reconcile, clock, intro, restart.
- [mod-patches-strip](mod-patches-strip.md) - stripping the sim, the UI and the options menu.
- [mod-patches-agents](mod-patches-agents.md) - agents are not colonists; the colonist bar.
- [mod-patches-misc](mod-patches-misc.md) - background running, real-time durations, loading screen.
- [mod-sidebar](mod-sidebar.md) - `AgentSidebar` and its core layout and selection behavior.
- [mod-sidebar-navigation](mod-sidebar-navigation.md) - shared views, tabs, filtering, and vanilla chrome shifts.
- [mod-ui-chrome](mod-ui-chrome.md) - shared widgets, layout, usage readout, top bar.
- [mod-ui-identity](mod-ui-identity.md) - UI geometry, spacing, and color schemes.
- [mod-ui-files](mod-ui-files.md) - the files view and its icons.
- [mod-markdown](mod-markdown.md) - native Markdown previews and their file boundary.
- [mod-ui-search](mod-ui-search.md) - workspace search and its result pager.
- [mod-icons](mod-icons.md) - the Codicons bake out of a Nerd Font, and the Icons lookup.
- [mod-ui-git](mod-ui-git.md) - the git view, the diff pager, and the third tab.
- [mod-ui-rowactions](mod-ui-rowactions.md) - view/edit/diff on a hovered row, in both trees.
- [mod-terminal](mod-terminal.md) - terminal input, sizing, title bar, and key routing.
- [mod-terminal-history-warmup](mod-terminal-history-warmup.md) - terminal history cache and warm-up behavior.
- [mod-terminal-rendering](mod-terminal-rendering.md) - terminal themes, fonts, selection, and links.
- [mod-content-views](mod-content-views.md) - one window, and what fills it.
- [mod-ui-windows](mod-ui-windows.md) - the dialogs and Settings pages.
- [mod-ui-rework](mod-ui-rework.md) - shared flat UI controls and layout rules.
- [mod-settings](mod-settings.md) - `ModSettings`, and why a knob lives there.
- [settings](settings.md) - the user-facing vocabulary and apply/confirmation rules.
- [window-fullscreen](window-fullscreen.md) - the Unity popup and Xwayland fullscreen contract.
- [vscode-registries](vscode-registries.md) - how VS Code wires actions, keys and settings into one system.

## Jukebox and in-game flavor

- [mod-jukebox](mod-jukebox.md) - station catalog, daemon playback, and the menu.
- [mod-jukebox-library](mod-jukebox-library.md) - local OST playback, likes, recognition, and history.
- [mod-eco](mod-eco.md) - eco mode: the board stops and the menu's background stands in.
- [mod-background](mod-background.md) - the baked menu background: the ramp, the grid, the walk.
- [mod-plague](mod-plague.md) - the dead ground.
- [mod-easter-eggs](mod-easter-eggs.md) - small hidden interactions.
- [mod-worksite](mod-worksite.md) - what a working agent builds.
- [skyfallers](skyfallers.md) - dropping a thing out of the sky.
- [mod-player-pawn](mod-player-pawn.md) - user-controlled human colonist with fireball.
