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
| Learning-helper concept panel (top-right) | `RimWorld.LearningReadout.LearningReadoutOnGUI` |

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

### Inspect-pane tabs

`Patch_InspectTabs` (also in `StripUI.cs`, applied from the bootstrap) postfixes
`Verse.InspectTabBase.IsVisible` and forces these pawn tabs hidden: **Bio**
(`ITab_Pawn_Character`), **Needs**, **Health**, **Gear** and **Social**. The base
getter is virtual and only Health inherits it - the rest override without chaining
up - so each override getter is patched too, same trick as the main buttons.

The inspect pane itself stays (name label, selection glue); tabs we don't name
(Records, Log, ...) and every non-pawn tab are untouched.

### Inspect-pane pawn overview

Two more patches in `StripUI.cs` empty out the rest of a pawn's inspect pane, so
only the name and the kept tabs remain:

| Dropped | Method patched |
| --- | --- |
| Top-right buttons: Info card, hostility response, rename | `RimWorld.MainTabWindow_Inspect.DoInspectPaneButtons` (prefix; zeroes `lineEndWidth` so the name keeps full width) |
| Health / Food / Mood bars, timetable & area selectors, and the gender/age inspect line | `RimWorld.InspectPaneFiller.DoPaneContentsFor` (prefix, skipped only when the selection is a `Pawn`) |
| "Select next thing in this cell" overlay button | `RimWorld.MainTabWindow_Inspect.ShouldShowSelectNextInCellButton` getter (postfix forced `false`; the button is drawn in `InspectPaneOnGUI`, separate from the pane buttons) |

Non-pawn selections (zones, storage, buildings) draw both normally.

## Interaction

Two ways to "play" a pawn are taken away, in
`mod/Source/SlopWorld/Patches/StripInteraction.cs`:

- **Selection** (`Patch_Selectable_ColonistsOnly`) prefixes
  `RimWorld.Selector.Select` so only colonist pawns select. Every path - single
  click, drag box, colonist bar - funnels through `Select`, so items, plants,
  buildings and terrain become unclickable while a colonist (and its Terminal
  gizmo) stays reachable.
- **Draft** (`Patch_Hide_Draft`) prefixes the internal
  `RimWorld.Pawn_DraftController.GetGizmos` to return nothing. `Pawn.GetGizmos`
  pulls the draft command straight from there, so the gizmo disappears and the
  daemon keeps sole control of the pawn.

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
- The inspect pane's remaining tabs (**Records**, **Log**) still show frozen
  sim data; the play-the-colony tabs are dropped above.
