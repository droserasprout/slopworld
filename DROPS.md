# Drops

What SlopWorld removes from vanilla RimWorld. The game is a viewer for agent
sessions, not a colony you play, so most of its own systems and UI are stripped.

Unless noted, stripping is **unconditional**: it happens whenever the mod is
loaded, with no setting to turn it back on. Being loaded is the switch.

## Kept on purpose

These stay because they serve the agent view:

- **The map, camera and pawn rendering** - the ambient status board.
- **Selection** - click a colonist to get its Terminal gizmo.
- **Colonist bar** (top) - quick selection of agent colonists.
- **Inspect pane** (bottom-left) - detail on the selected pawn.
- **Menu** main button - save / load / options / quit.
- **Agents** main button (`SlopWorld_Agents`) - the session manager.
- The **status overlay** and **Terminal** gizmo the mod adds itself.

## UI drops

All in `mod/Source/SlopWorld/Patches/StripUI.cs`. Each hides a per-frame draw by
prefixing it to return `false`.

| Dropped | Method patched |
| --- | --- |
| Message toasts (top-left) | `Verse.Messages.MessagesDoGUI` |
| Letter stack (right edge) | `RimWorld.LetterStack.LettersOnGUI` |
| Alert readout (right edge) | `RimWorld.AlertsReadout.AlertsReadoutOnGUI` |
| Resource counts (bottom-left) | `RimWorld.ResourceReadout.ResourceReadoutOnGUI` |
| Date / temp / season / **speed & time controls** (bottom-right) | `RimWorld.GlobalControls.GlobalControlsOnGUI` |
| Mouseover terrain / thing readout (bottom-left) | `RimWorld.MouseoverReadout.MouseoverReadoutOnGUI` |

### Bottom-bar buttons

`Patch_MainButtons` (same file, applied from the bootstrap) postfixes
`MainButtonWorker.Visible` (base getter plus the one override,
`MainButtonWorker_ToggleMechTab`) and forces every button hidden except the
keep-set `{Menu, Inspect, SlopWorld_Agents}`.

Hidden: **Architect** (all building), **Work**, **Schedule**, **Assign**,
**Animals**, **Wildlife**, **Research**, **Quests**, **World**, **History**,
**Factions**, and any DLC tabs (Mechs, Ideoligion, ...).

Notes:
- Game speed is no longer changeable from the UI, but the **spacebar** pause
  keybind still works. The sim is frozen anyway (below), so speed is moot.
- `Inspect` is kept because it backs the inspect pane, not for a visible button.

## Notifications

Covered above: the message, letter and alert stacks are all suppressed. Letters
are only hidden from view, not swallowed at the source, but with the storyteller
frozen almost nothing generates them.

## Starting colonists

`mod/Source/SlopWorld/Sim/StarterPurge.cs` (a `GameComponent`).

The vanilla scenario lands three starting colonists. About **two seconds** after
they touch down, each one is **exploded in a shower of blood** and wiped from the
colonist bar, leaving the map to the agents:

- ~40 blood-filth splats scattered in a 3.5-tile radius, plus a `Bomb` explosion.
- The pawn is then killed, its corpse destroyed and the pawn despawned, so
  nothing lingers in the colonist bar.

Guards:
- Runs **once per game**; a persisted `starterPurgeDone` flag stops a reload from
  re-triggering it.
- Only fires on a **fresh landing** (`TicksGame <= 2000`). Loading an existing
  colony - including adding this mod to an old save - never touches its pawns.
- **Agent colonists are excluded** (`AgentColony.IsAgentPawn`), so a session that
  spawns a pawn in that first window is not caught in the blast.
- If the game is paused when the pawns land, the purge nudges speed to normal so
  the pods open and the blast animates.

## Sim drops (pre-existing)

`mod/Source/SlopWorld/Patches/StripPatches.cs` declines to tick the sim rather
than patching out individual systems. Unlike the drops above these are gated by
the **"Strip the colony sim"** mod setting (`StripSim`, default on):

- Needs (hunger / rest / joy), health (disease / bleeding / hediffs), aging,
  mental breaks, and the storyteller (raids / events / quests / weather).

## Not dropped (candidates for later)

Left intact for now; revisit if they get in the way:

- The new-game scenario **intro dialog** and character/landing config screens.
- The **Esc menu** and main menu.
- Inspect-pane **ITabs** (Needs, Health, Gear...) still show frozen sim data.
