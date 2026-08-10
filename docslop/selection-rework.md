# Selection rework - the session is what is selected

Planned, not built. Delete this note when it is.

## What is wrong

With the terminal hidden, a left click on a host or ephemeral row does nothing:
`AgentSidebar.Click` jumps the camera at `row.Pawn` and a ghost row has none
([mod-sidebar](mod-sidebar.md)). The dead click is the symptom. The cause is that
*every* way to act on a session is derived from a pawn:

| affordance | where | how it finds the session |
| --- | --- | --- |
| Terminal / Stop / Start | `Patch_Pawn_GetGizmos` | `colony.SessionOf(pawn)` |
| `T` / `P` | those gizmos' `hotKey` | only with a pawn selected |
| comma / dot | `ShortcutKeys.ShortcutKeysOnGUI` -> `ThingSelectionUtility.SelectNext/PreviousColonist` | walks colonists |
| Alt+Num | `TerminalHotkeys.FocusSlot` | `InBarOrder()` -> `PawnOf` |

A ghost has no pawn and falls out of all four at once. Spawning one for it - an
animal, a building - buys all four back in a stroke, which is the honest appeal of
it, and pays a whole pawn lifecycle for four affordances while reversing the
reason `AgentColony` spawns nothing for an ephemeral session in the first place
([mod-sim](mod-sim.md)): a drop pod every time a file is read.

A hover strip on the row is not the answer either. `RowActions` is the same
*errands* the menus carry ([mod-ui-rowactions](mod-ui-rowactions.md)), but an
action button is a button with a key on it and a strip under the mouse has none.

## What vanilla actually requires

Checked against `Assembly-CSharp.dll`, not assumed:

- `Verse.ISelectable` is three methods - `GetGizmos`, `GetInspectString`,
  `GetInspectTabs`. No position, no `Thing`.
- `GizmoGridDrawer.DrawGizmoGridFor` takes `IEnumerable<object>` and reaches each
  one through `isinst ISelectable`. It never asks for a `Thing`.
- A gizmo's `hotKey` is read in `Command.GizmoOnGUI`, per gizmo, so **the key
  rides with the button** whatever supplied it.
- The seam is `MapGizmoUtility.MapUIOnGUI()`: it fills `tmpObjectsList` from
  `Find.Selector.SelectedObjects` and hands it to the drawer. It returns early
  unless the open tab is none or `Inspect`.
- `Selector.SelectInternal` **refuses** anything that is not a `Thing`, `Zone` or
  `Plan` - `Log.Error("Tried to select {0} which is neither a Thing, Zone, or
  Plan.")` and returns. So sessions do not go in `Find.Selector`, and do not need
  to: `MapUIOnGUI` is the only consumer this wants.

## The model

**One current session, owned by the mod.** A `SessionSelectable` wraps a session
name and implements `ISelectable`; its `GetGizmos` is `Patch_Pawn_GetGizmos`'s
present body with `SessionOf(pawn)` taken off the front. `MapUIOnGUI` is patched
to hand the drawer that object.

- **Set by** a sidebar row click, comma/dot, Alt+Num, opening or switching a pane,
  and selecting an agent's colonist on the map. That last one is a one-way sync -
  pawn selected, current session follows - so the two selections never disagree.
- **Shows** the same gizmo row for every session, `T` and `P` on it, ghost or
  agent. Action buttons are not lost by dropping the pawn; they stop being a
  property of having a body.
- **The camera is the only difference between the kinds**, and it is a difference
  in the world rather than in the interface: an agent row jumps it because there is
  something to look at, a ghost row leaves it because there is not.
- **A walk changes the selection and nothing else.** Never opens, closes or
  switches a surface. Pane closed, comma/dot moves the gizmo row down the column
  and the camera with it where there is a pawn; pane open, it switches panes. One
  rule, and no mode flip where the walk crosses from an agent to a ghost.
- **Opening a pane stays explicit** - F12, `T`, the Terminal button. Which is also
  why the left click no longer has to mean two things.

## What changes

- `SessionSelectable` (new), and `PawnGizmoPatch` loses its `SessionOf` lookup.
- A patch on `MapGizmoUtility.MapUIOnGUI`. Note `GizmoGridShift` already moves that
  same grid's `startX` past the sidebar ([mod-sidebar](mod-sidebar.md)).
- `AgentSidebar.Click`: a row sets the current session; a row with a pawn also
  clears and jumps as it does now.
- Comma/dot: drop `PreviousColonist` / `NextColonist` from `StripKeys.Keep`, ship
  `SlopPrevSession` / `SlopNextSession` in `Defs/KeyBindings.xml`, and prefix
  `ShortcutKeys.ShortcutKeysOnGUI` - the one place vanilla reads those two. The
  walk follows `AgentSidebar.Rows`, ghosts included and folds respected, because it
  follows the eye. `Rows` is empty in the files and git views, so it needs the
  fallback `TerminalHotkeys.AnyLive` documents for F12.
- Alt+Num stays portraits only. A number is an index somebody memorised; a walk is
  positional. Including ghosts in the second breaks no mapping held in a head.
- Over a pane bare comma/dot belong to the agent, so the walk there is Alt+comma
  and Alt+period, beside `Alt+Num` in `HandleKey` / `ChromeKeys`.

## The loose end

An agent's inspect pane is the pawn's and a ghost has none, so the buttons would
sit under an empty slot half the time. Uniformity says draw one small session pane
there for **both** kinds - name, state, and the dir or title - and hang the gizmos
under it. `InspectPanePatch`, `ChromeShift.Reposition` and `GizmoGridShift` already
reshape that area, and a colonist's own inspect string is mood and needs, which
nothing here reads.

Check before building: whether `MapUIOnGUI`'s early-out is still satisfied, the
main button row being patched heavily ([mod-patches-strip](mod-patches-strip.md)).
