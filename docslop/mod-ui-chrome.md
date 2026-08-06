# Shared UI chrome

## `SlopWidgets`

The chrome drawn in more than one place, and the colours that mean the same thing
wherever they are drawn - `Bad` for an error that stays on screen, `Well` behind
a text area. **Not a theme**: `TerminalTheme` is the pane's and answers to a
terminal's rules; everything else draws from here. `Fail`
is here because every refusal the player is shown wears the same prefix, and a
message that skipped it would be the one that did not look like ours. Static
helpers rather than a base class's methods, because half the callers are not
windows - `ConfigPage` is a category of the options menu and `FilesView` is a
panel.

**The ramp** is five rungs down from white, named for what a colour is *for*:
`Lead` (the thing the row is about), `Name`, `Dim` (a second line), `Faint`
(glanced at - an age, a group heading), `Off` (a tab you are not on). Plus the
surfaces: `Panel`, `Edge`/`EdgeLit`, `Well`, `RowBg`/`RowOn`. The sidebar, top
bar and files view each used to mix their own - eleven greys for five
intentions, two pairs byte-identical and three more within a hundredth of each
other. A view wanting a contrast picks **two rungs**; it does not mix a sixth
grey. `Lead` is not `Text` because `Verse.Text` is what every font and
measurement in the mod goes through.

`SlopListWindow<T>` is the other half: agents, projects and shortcuts are one
window drawn three times, so the ctor flags, the size, the header, the scrolling
list and the footer row live here and a subclass says only `Title`, `RowH`,
`EmptyNote`, `Rows`, `DrawRow`, `DoFooter`. The empty list's other answer - the
daemon being down - is `SlopWidgets.Unreachable` and is nobody's to state.
`Toggle` cannot be inherited (statics are not virtual), so each window keeps its
own line over `SlopWidgets.ToggleWindow`, which takes a **factory** rather than an
instance so nothing is built for a toggle that turns out to be a close.

## Headings, heights and gaps

`Header` is `Title` plus `Status`: the name left in `Lead`, the daemon as a
dot-and-text pill laid out from the **right** end, a hairline under both.
Nothing measures the title any more - it used to start the status at the title's
width plus a figure, and before that at a figure per window, three of them nudged
by hand to clear three different titles. `Title` alone is what a dialog wears.
`SectionHeading` is `Faint` with a rule filling the rest of the line, which is
what stops a form reading as a wall of body text; every group of controls in the
mod has one.

**Every height is off the font.** `LineH` is `Text.LineHeightOf(Small)` ceilinged,
and `FieldH`/`RowH`/`HeaderH` are that plus padding - a border and a line of
glyphs cannot be the same pixels, which is why a press is 30 and not 20 and why a
field is 30 too, so the two line up on one row. A figure eyeballed against one
font crops descenders on every other; `AgentSidebar` has asked rather than written
since its labels lost their bottom row to one.

**Every gap is one of four**: `GapXS`/`GapS`/`GapM`/`GapL`, 4/8/16/24, named for
what the space is between. There were eighteen figures before. `PageCaption`,
`PageBody` and `FooterBar` are the shape the four option-menu pages share, and
`FooterBar` is also the three dialogs' - each had written `yMax - 34f` with a
height of 32 for a button that stands 30.

## `Slab` and the flat controls

`Slab` is a rounded rectangle - `Fill`, `Outline`, `Box`, and `Raised` (a hairline
of shadow under it and one of light along the top inside; both go while pressed).
**Only the corners are a texture.** Nine-slicing one rounded box draws a line along
every internal seam, bilinear filtering sampling half a texel past each tile; so the
four corners are drawn at their own size and every straight run between them is flat
colour off `BaseContent.WhiteTex`, which has no texel grid to disagree about.

Everything is snapped to the **screen** grid, not the GUI one - at UI scale 1.75 a
whole GUI coordinate is 1.75ths of a pixel, and a pixel split by two quads belongs to
either, neither or both, which is a dark seam, a light one or nothing depending on the
control's absolute position. Corners are baked at `R * UIScale` and rebaked when that
changes, so the curve is never resampled. `Snap` goes out to the screen and back **by
arithmetic** off a two-point sample of the transform: `ScreenToGUIPoint` is not the
inverse it is documented as inside a group, and the return trip drops the offsets of
the several a window draws in. `TerminalWindow.SyncSnap` does the same thing for the
pane's rows and is where the trick came from - see [gotchas](gotchas.md).

`SlopWidgets.Button` takes a `Btn`: `Default`, `Primary`, `Danger`, `Ghost`. Adwaita's
shape, not its colours - the border is **darker** than the face, and the face is
**opaque**, which is the half that took two passes to get: a dark border round a face
at 0.065 alpha encloses nothing, and what is left reads as a widget toolkit from
twenty years ago. Ghost is the one face still drawn through. `Bar` lays a footer out
from the end each button belongs to, which is what replaced the `+138f`/`+276f`
offset chains three windows each kept, and `Bar.Rest` is what is left between the two
ends - where the three pages that put an error beside their Save now put it. `Button`
takes an `on`: a press that cannot be made yet drains toward the window and sits level
with it, because a Save that vanished with the daemon reads as a page with no save.

`Field`, `Area` and `Checkbox` are the forms. `Listing_Standard`'s own `TextEntry` and
`CheckboxLabeled` draw vanilla's chrome, so the row comes off `l.GetRect` and ours goes
into it - the listing's column and gap handling is what was worth keeping. Each takes a
**name**, `GUI.SetNextControlName` wanting one and two boxes sharing a name sharing a
focus. `Field`/`Area` take an `on` too: off, the text is drawn *as text* rather than into
a control that took keystrokes and dropped them on the next frame, which is what greying
used to mean here. `Area` takes a `frame` for the one inside a scroll view - the box there
belongs round the **view**, since one as tall as the content puts its border off the
bottom of the window. `Checkbox` takes a `locked` for a preset the command asks for
anyway: same tick, face and label down to `Faint`, no hover on a row that is stating
rather than asking.

The one text field left outside all this is the command palette's, which keeps its own
control name and drives its own focus.

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
answer in one place, so nothing else has to know a layout exists. `Hidden` is the
other question - screenshot mode, F11 - and is deliberately not folded into
`Shown`: the insets are a layout answer, and moving them for an interface that is
merely not being drawn would shuffle everything the frame the key is pressed. The
sidebar needs neither, riding `ColonistBarOnGUI` which vanilla already filters;
`TopBar` and the inspect pane's Edit button are drawn from paths that run before
that gate, so each asks. See [gotchas](gotchas.md).

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
