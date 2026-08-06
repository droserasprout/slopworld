# Shared UI chrome

## `SlopWidgets`

The chrome drawn in more than one place, and the colours that mean the same thing
wherever they are drawn - `Dim` for a second line about the thing on the first,
`Bad` for an error that stays on screen, `Well` behind a text area. **Not a
theme**: `TerminalTheme` is the pane's, and a view with a palette of its own
(`FilesView`, `AgentSidebar`, `TopBar`) is naming contrasts for one panel. `Fail`
is here because every refusal the player is shown wears the same prefix, and a
message that skipped it would be the one that did not look like ours. Static
helpers rather than a base class's methods, because half the callers are not
windows - `ConfigPage` is a category of the options menu and `FilesView` is a
panel.

`SlopListWindow<T>` is the other half: agents, projects and shortcuts are one
window drawn three times, so the ctor flags, the size, the header, the scrolling
list and the footer row live here and a subclass says only `Title`, `RowH`,
`EmptyNote`, `Rows`, `DrawRow`, `DoFooter`. The empty list's other answer - the
daemon being down - is `SlopWidgets.Unreachable` and is nobody's to state.
`Header` lays the status line out from the title's *measured* width, three of
those figures having been nudged by hand to clear three different titles.
`Toggle` cannot be inherited (statics are not virtual), so each window keeps its
own line over `SlopWidgets.ToggleWindow`, which takes a **factory** rather than an
instance so nothing is built for a toggle that turns out to be a close.

## `MenuToggle`

A float-menu row that states one fact about itself: the label, and a tick or a
cross where the row ends. `FloatMenuOption` has no ticked state - `Disabled` is the
nearest thing and it greys the row out, which reads as broken rather than as off -
so the mark goes in the `extraPartOnGUI` vanilla already reserves, with
`extraPartRightJustified`. The extra part answers **false** always: its return is
"this was clicked", and the row under it has already taken the click. The tick is
`SlopWidgets.Yes` and the cross is `Dim`, red being for something having gone
wrong. `UI/MarkIcon.cs` draws both the way `ShieldIcon` draws its wall. The
jukebox's Mute and Stop-on-exit rows are the users.

## `SmoothScroll`

Held in place of the `Vector2` a scroll view used to keep. IMGUI does the wheel in
`GUI.EndScrollView` - `delta * 20`, applied whole, next frame - which reads as the
notch on a mouse and as teleporting on a touchpad, where the driver sends that
same notch-sized delta dozens of times a gesture. `Begin` takes the event before
the scroll view sees it, puts the delta in a **target**, and eases the drawn
position toward it (`Tau` 0.05s, unscaled - a paused game is not a still list).
`End` compares what came back through the `ref` against what went in, so a
scrollbar drag or a clamp against shrunken content becomes the new target rather
than something to ease back out of.

The `e.Use()` is load-bearing twice: without it ours and the scroll view's handling
both land and the list travels double, and an *enclosing* scroll view takes the
gesture off the inner one. It is skipped when there is nothing to scroll, or a box
showing all its content would pin the page behind it.

On the sandbox tab's two boxes and `PresetList` only. The terminal pane has its own
wheel path (`TerminalWindow.HandleWheel`) and is still line-quantised.

## `SlopLayout`

Which chrome this install wears and how much room the rest of it has to leave:
zero in the strip layout, and `AgentSidebar.Width`/`TopBar.H` in the other. One
answer in one place, so nothing else has to know a layout exists.

## `UsageReadout`

Draws the quota windows ([daemon-usage](daemon-usage.md)) as the game's own
resources, counting what is **left**. A `MapComponent`, so it sits behind every
window. Icons are assigned per key from `Known`/`Pool` and remembered, or they
would move between polls - statics, since the same numbers are drawn from the top
bar as well and an icon that changed with the layout would be a different resource
for the same window. `DrawStrip` is the same rows along a line, laid out from the
right so the first window keeps its place as later ones come and go.

- `Chosen` beats `Known` beats the pool: `Settings.usageIcons` is `key=defName` a
  line, and a key with no line - or one naming a def this build has not got -
  falls back to the pick that was always made. `Choose` writes and `Invalidate`
  drops the table, which is the only thing that reopens the question.
- A money row is `unit: usd` with an amount, and its limit is read as **unsaid at
  -1** rather than at zero: a wallet with nothing in it has $0 left, and a limit of
  zero rounded into a percentage would draw an empty account as a full bar.
  `balance` is the OpenRouter row and wears gold, `spend` silver - what is left of
  a wallet and what is left of a budget are different questions.

## `TopBar`

The sidebar layout's one line across the top: the current agent on the left, the
wall clock in the middle, the quota on the right. It starts where the column ends.
Drawn from `UsageReadout` on the map and from `TerminalWindow` over a pane (for
the reason the colonist bar is), and `DrawOnMap` stands down while a pane is up
rather than registering a second copy's tooltips underneath it.

With a pane open this is also its **title bar** - the gear and the cross move to
the right end and `TerminalWindow` draws no header of its own, so the pane gets
the whole screen below the line. The agent's own terminal title is what it says;
only a subscribed session has one, which in practice is the one whose pane is
open, and the state stands in for the rest.

## Small stuff

- `CoreTip` hangs a loading-screen tip on the persona core, rolled once per hover.
- `DeadCursor` replaces the pointer with the Tame designator's hand.
- `MenuBackground` bakes filters from whatever background this install ships and
  caches them to disk; `Patch_MenuBackgroundRot` hooks the **draw** rather than
  `Init`, because the loading screen draws the same background without going near
  `Init`. Two variants, chosen by `grandmaMode` and named in the cache key so both
  survive on disk: `Rot` darkens and burns the planet, `Sparkle` lays a rainbow
  and a blinking constellation over it. The rot's playback breathes around a
  depth; the sparkling one is a loop, and its stages are baked to close - the
  sheen slides one whole hue period and every star blinks a whole number of times,
  so stage 29 meets stage 0 with no cut. A toggle with the menu up is caught in
  `Current`, the caller having stopped handing it a source.
