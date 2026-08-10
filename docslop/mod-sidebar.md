# `AgentSidebar` and `ChromeShift`

`AgentSidebar` is the other shape the colonist-bar swap can take
([mod-patches-agents](mod-patches-agents.md)), and `SlopLayout` is which one: a
column down the left, agents under the project they run in, portrait and three
lines apiece.

Only the geometry moves - `Place` writes the same `cachedDrawLocs`, so the state
icons, the brackets, the "+" and the click that opens a pane are all still the
bar's. What is drawn *around* the portraits goes down from the same
`ColonistBarOnGUI` call: the panel and the headings in the prefix, the labels in
the postfix, which is what puts the column over a pane as well as on the map.

- **Grouping is a reorder, not a resort**: entries keep their indices and the
  column decides which y each one gets, so a drag on the bar still means what it
  meant. `Order` is alphabetical with the projectless bucket last, because a
  dictionary's own order is not stable between frames.
- The panel is the full height of the screen and the top bar starts where it ends,
  rather than the bar crossing the top of it: hung underneath one, the corner
  above the column is a hole the map shows through.
- A row is as tall as the *portrait* (overhang and all) or as tall as the labels,
  whichever is more. Rows are `48+32` apart - vanilla's own vertical pitch, which
  leaves room for a head to poke into the gap above it - floored at `TextH`, and
  with three lines that floor is the usual answer. Only the portraits shrink to
  fit; headings and the "+" are a fixed cost, so `Fit` **solves** for the scale
  rather than stepping it down. It stops at the scale where that floor takes over
  rather than at `Floor`: below it the rows no longer close up and every pixel off
  a face buys nothing, so the column runs off the bottom with its portraits
  legible. The "+" is pinned to the foot of the panel and `Fit` reserves its room
  either way, so the rows never run under it.
- **The three lines**: the name; what the agent is doing and for how long; what the
  app calls itself. All three are one lookup on the hub and none is parsed out of a
  pane - the state is the daemon's word, the age is `now - state_since` done here
  so nothing is sent to keep a countdown in step, and the third line is the
  session's OSC title, falling back (dimmer, being ground rather than word) to the
  directory leaf, or to "temporary" for an ephemeral agent. The age is laid out
  from the right so the times line up down the column. A `bell` takes the end of
  the *name* line, that being about the agent rather than its posture, and is
  `Icons.Bell`. A **down** agent wears no age at all - it is stopped, and how long
  it has stood there is a fact about nobody's clock - so its line two is the state
  alone.
- **Ghost rows** are the ephemeral sessions: a host shell, or anything else run by
  hand. No colonist is spawned for one any more (`AgentColony` skips them, and retires
  a pawn bound to one), so the bar knows nothing about them and they are laid out from
  the hub's own list instead - `Ghosts` by project, `TopGhosts` for the ones belonging
  to none. One line, the name or the app's own title, no portrait and no state: "idle"
  of a shell is a fact about nothing, and the row goes when the process does. Viewers,
  editors and diffs are routed out of this list: they are drawn as the same one-line
  rows at the top of the Files or Git body, before that view's grouped projects. They
  are off Alt+Num - a number there is a portrait - and their cost to `Fit` is fixed,
  text not being what shrinks. A routed row wears its mark **in front** of the name
  (`RowActions.Of`, off the command and not the label): the name is a title the app
  wrote and long enough to be cut, so a mark after it would be the first thing to go.
  A host shell remains in Agents and wears none - the name is all it is.
- `Absorb` eats the mouse over the panel, last of all. Without it a press starts a
  drag-selection on the ground behind the panel and a right-click orders a
  colonist to walk there.
- The options menu is a **content view** now, not a window over the chrome
  ([mod-content-views](mod-content-views.md)), and the column answers presses under
  it because nothing absorbs above it any more. `ColonistBarStrip.Interactive` is
  back to a plain `!Blocked`; the `OptionsOpen` exception it carried was written for
  this and bought nothing. `Mouse.IsOver` is still not used for any of these checks:
  `ColonistBarStrip.MouseOver` calls `Contains` directly, bypassing `IsInputBlockedNow`
  (which would return true, the current window not getting input). The press checks
  read `Event.current.rawType` rather than `type`, which recovers a MouseDown that
  `HandleEventsHighPriority` Used - but **only** one that arrived at all, which under
  an absorbing window is none (see the entry in `gotchas.md`; measured, not assumed).
  `Grip` reads no part of its gesture off an event even so, being the one that also
  has to survive a release dragged off the edge of the screen: it samples
  `Input.GetMouseButtonDown(0)` while the pointer is over the edge, follows
  `Event.current.mousePosition` for as long as
  `Input.GetMouseButton(0)` holds, and ends when it does not. F1 and F12 are the
  chrome window's own (`TerminalWindow.ChromeKeys`), whichever view has the body.
- `Menus` and `Grip` are taken in the **back** pass, before the bar's own draw,
  because the bar swallows a right-click over a portrait to keep it off the map -
  asked for after it, a row menu would open over only half a row. A heading folds
  on the left button and opens its project on the right; a row opens its agent's
  on the right.
- `HeadMenu` is Edit, Duplicate, **`Terminal (host)`**, Delete. That third one is a
  shell in the project's directory and *not* in its sandbox -
  `SessionHub.RunHostShell` -> `POST /api/run` with `host` set, which is the only
  road to an unsandboxed session and is why it says so on the label
  ([wire-protocol](wire-protocol.md)). Command and label are both left empty: the
  shell is `$SHELL` and the entry is named for the project and that shell together -
  `slopworld-zsh` here - and neither answer is the game's to give. The files view's
  heading carries the same option ([mod-ui-files](mod-ui-files.md)).
- **A fold parks its bucket's locs off screen** rather than merely skipping them:
  the bar hit-tests against the same list it draws from, so parking is how an
  entry leaves both, and no `Row` goes in either, which takes a folded agent off
  Alt+Num. `TerminalHotkeys.AnyLive` falls back to the hub for that reason - a
  fold is about the column, and F12 is meant to always open something.
- `Width` is the panel's own edge dragged, `sidebarWidth` in mod settings, written
  on release rather than every drag frame. Clamped on the way *out*, against the
  screen as well as the figure: a width saved on a wide screen and read on a narrow
  one is a column with no map beside it. The inspect pane is told by hand
  (`Patch_MainTabWindowShift.Reposition`). Folds ride along in `foldedProjects`,
  one name per line - a project is the daemon's rather than a colony's, so neither
  belongs in a save.
- `Patch_SidebarPawnLabel` declines vanilla's name-under-the-portrait while the
  column draws: the cell is 24px wide there and the name lives beside it.
- `Patch_SidebarPortraitDraw` replaces the whole of `DrawColonist` with a
  **close-up of the head** where vanilla draws a body cropped at the hips: the blue
  background, the mood atlas, bar, overlay and gradient all go with the body, and a
  stopped agent is greyed rather than crossed. What is left is highlight, portrait,
  selection brackets, icons, dead overlay - the brackets go *after* the portrait,
  unlike vanilla, because a head cropped to the square is opaque over its own
  corners and would eat the inner arm of each one. It draws in the square `Place`
  laid out rather than working one out - the row, the labels, the click and the
  portrait are four readers of one table, and the vanilla geometry it replaces (a
  46x75 texture hung off the bottom of a 48x48 cell) describes a shape no longer
  being drawn.
  Icons are handed the *face box* for the same reason: anchored to the cell they
  float in the middle of a face. Framing is `FaceZoom` and the pawn's own head z
  ([gotchas](gotchas.md) - `cameraOffset.y` frames nothing), and the head is
  rendered standing even when the pawn is downed, `RenderPortrait` turning a downed
  pawn 85 degrees out of a shot this tight.
- `Drawing` is cleared from the **finalizer** as well as the front pass, a postfix
  not running when the original throws and that flag being what hides every pawn
  label on the map.

## Three views

`Tabs` is the selector: three mono-grey icons across the top of the panel, off the
icon bake ([mod-icons](mod-icons.md)), and from the right end each tree's own one switch - the
dotfile switch in the files view, the refresh button in the [git
view](mod-ui-git.md). Nothing else: the cog and the hamburger that used to sit beside
that switch are on the status bar now ([mod-ui-chrome](mod-ui-chrome.md)),
neither of them being about a view. Only the body changes - the panel, the width, `Grip` and
`Absorb` are the panel's and are drawn once whichever view has it, which is the
whole reason this is a strip and not a second sidebar. `TabH` is reserved in
*all three*, so switching moves nothing below it.

The view is also switched by the keyboard: `FocusTerminal` is what F12 (opening a
pane) and Alt+Num (while a pane is open) take, because both are about an agent and
the other two views are not. `Show` releases every pager but the arriving view's -
the `less` the file tree opened, the diff the git tree did - so leaving a view never
leaves a reader running behind it.

In the [files](mod-ui-files.md) and [git](mod-ui-git.md) views, `Place` parks every
loc and lays out no `Row`, so the bar draws and hit-tests nothing - the same move a fold
makes, with no second call site and no new patch. It still runs `Bucket`, because
`Sessions()` is what `AgentColony.InBarOrder` and so Alt+1..9 read: a fold takes an agent
off the numbers because it takes it off the column, but switching views hides every agent
equally and is not a fold, so `Sessions()` answers off the buckets there instead of off
the rows. `Menus` stands down; the view with the body takes its own clicks, including the
routed viewer/editor/diff rows before the tree. The "+" does **not** go with the agents -
see below.

## The add strip

One button at the foot of the panel, in every view. `AgentSidebar.AddBar` is where
it is, `DrawAdd` draws it and `AddClick` answers it, all from the back pass, and
what it adds is asked on the click rather than answered by whichever tab is up: a
float menu of **Project / Agent / Shortcut**, opening the same three editors the
command palette does. `Body` has the strip taken off it in all four views, so a
full list scrolls above the button instead of under it, and the shortcuts view -
which had grown its own "+" and its own reserved strip - now draws neither.

`Place` lays no rect out for it, only `Fit` reserving the height, and
`ColonistBarStrip.ShowAdd` is the one question left there: whether the panel is on
screen at all. `Patch_ColonistBarAddButton` is gone with the rest - drawing the
button from a postfix on `ColonistBarOnGUI` was what made it the agents view's, and
what made `Absorb` step around its rect by geometry, Harmony not saying which of
two patches on one method runs first. Asked from the back pass it is simply first,
and `Absorb` needs no exception.

The plus is `Icons.Add` at 16px, not a `"+"` in `GameFont.Medium`: Medium is as
large as vanilla's fonts go and drew a hairline in a 26px strip
([mod-icons](mod-icons.md)).

## `ChromeShift` - what the column does to the rest of the interface

- The bottom button row is laid out contiguously from zero to `screenWidth` with
  the last button widened to fill, so squeezing the whole line into the room right
  of the column is **one prefix on `MainButtonWorker.DoButton`** remapping the rect
  it was handed. No transpiler - this game's Mono has already refused one
  ([gotchas](gotchas.md)) - and `DoButton` is the one method every button's rect
  goes through.
- The inspect pane is anchored left (`x = 0`), so
  `MainTabWindow.SetInitialSizeAndPosition` gets a postfix; that runs on open and
  on a resolution change, so a layout toggled with the pane already up moves it on
  the next open rather than every frame. Dragging the column calls the same
  `Reposition`, or the pane sits still while the panel is pulled over it.
- `GizmoGridShift` is the other half of moving that pane: the gizmo grid's `startX`
  is `14 + PaneWidthFor(pane)` and is *not* shifted, so with the pane pushed right
  the first gizmos end up underneath it (the window stack draws after the map
  interface). The sidebar inset is added to `startX`. A static flag tells the
  bottom-of-screen grid (from `DrawGizmoGridFor`) apart from the architect menu's
  designator grid, which calls `DrawGizmoGrid` from inside its own tab window.
- Moving that window is not moving its tab row: `InspectPaneUtility.ExtraOnGUI` is
  called by the window *stack*, outside the window's group, so Log, Gear and our
  own Edit are screen coordinates laid out from the pane's width with the pane
  assumed to start at zero. `DoTabs` is wrapped in a `GUI.BeginGroup` instead of a
  rect being remapped: it lays the row out right to left from one figure, and the
  space it draws in is the only lever on that figure. An open tab's window is
  anchored `x = 0` the same way and is *registered* rather than drawn, so the
  group cannot reach it and `InspectTabBase.TabRect` is shifted on its own - once
  each, the group being gone by the time the stack draws it.
