# Agents are not colonists

- **`NoRescueAgents`, `NoStripAgents`, `NoHarmAgents`** - damage dies in
  `Pawn.PreApplyDamage`; the three ways an animal reaches an agent are closed one
  each. `NoBurningTheColony` closes both attachment and cell damage and spares the
  whole player faction.
- **`NoRelateAgents`** - vanilla builds a new pawn's relatives out of everyone
  alive, and `PawnRelationWorker_Parent.ResolveMyName` casts a parent's name to
  `NameTriple` where an agent's is a `NameSingle`. Zeroing the weight is all it
  takes; applied by hand because `GenerationChance` is virtual.
- **`AgentsCanBuild`** - roughly one backstory in five disables ManualSkilled,
  which takes Construction with it. The list `Pawn.GetDisabledWorkTypes` hands
  back is the pawn's own cache, so removing Construction from it is what makes the
  answer stick, and vanilla rebuilding the cache only means this runs again.
- **`SteadyHands`** - a `StatPart` on `ConstructSuccessChance` answering 1 for an
  agent. Vanilla rolls that stat once per work tick and a short roll eats the
  frame's materials and everything done to it. Added to the def at startup rather
  than patched into the driver: the roll is what wants changing, not the job.

# The colonist bar, kept and extended

`ColonistBarStrip`, `ColonistBarAddButton`, `ColonistBarStateIcon`,
`InspectPanePatch`, `PawnGizmoPatch`.

- `Patch_AgentNeverIdle` answers `IsIdle` false, so the daemon's word is the only
  thing that draws a clock.
- `Patch_NoPrioritizedWorkGizmo` removes "Clear prioritized work", which turns up
  despite nothing setting it: a `PriorityWork` read back from a save has a zeroed
  cell and `IntVec3` counts a zero as valid.

## `ColonistBarStrip`

The bar in *both* views. It prefixes `ColonistBarOnGUI` to point the bar's own
cached scale and draw locs at one shrunk, centred row in a `BarH`-tall band, and a
finalizer puts them back, so toggling a pane moves nothing.

- Over a pane the call must come from **inside** the window (`ColonistBarStrip.Draw`,
  from `TerminalWindow.DoWindowContents`) or the terminal paints over it, and
  `Suppressed` keeps the map-layer call from drawing a buried second copy.
- The "+" slot is reserved before the row is centred, so portraits do not shuffle
  sideways.
- `Blocked` is the strip declining clicks while something is stacked over the
  pane; on the map layer that never arises, `HandleEventsHighPriority` having
  already Used the event.
- The same swap has to go round **`ColonistBar.TryGetEntryAt`**: `Selector` asks
  that method while the map handles the click, by which time the finalizer has
  restored the vanilla layout, and the two layouts overlap for part of the row -
  which is why it read as some colonists selecting and some not. Only the
  outermost call owns the swap; the bar asks it of itself mid-draw, and restoring
  there would undo the layout being drawn.

The other shape that swap can take is [mod-sidebar](mod-sidebar.md).
