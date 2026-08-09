# Stripping the sim, the UI and the options menu

- **`StripPatches`** - the sim, killed by declining to tick it rather than by
  patching out systems one at a time.
- **`StripUI`, `StripInteraction`** - hiding a main button is not taking its tab
  away: two roads reach the Architect menu and neither looks at `Visible`, so
  `Patch_MainButtons` prefixes `MainButtonWorker.InterfaceTryActivate` and gates
  it on the same `Visible` the bar reads. A button missing from `Keep` never
  appears.
- **`StripOptions`** - General and Gameplay are removed from
  `AllDefsListForReading` so they take no slot in the column. Not `isDev`:
  dev mode is now a checkbox on our General page, and a category with `isDev=true`
  shows up as soon as the player ticks it. The defs stay in the database for
  callers who walk `DefDatabase<OptionCategoryDef>` and simply never draw.

## `SlopOptions` - our categories in vanilla's options window

`ConfigPage`, `TerminalPage`, `UsagePage`, `SandboxPage`, `AboutPage`, inserted at
0, 1, 2, 3 and 4. The daemon's
settings live there rather than in a window of their own, so there is one page a
knob is looked for on.

- It opens **inside** the chrome rather than over it - `SlopLayout.LeftInset` /
  `TopInset` off the corner, the rest of the screen - so the column and the line
  stay where they are and are drawn by the map layer as usual, with no copy needed
  here. The inset is dropped with no colony behind the window: the column hangs
  off the colonist bar and the line off a `MapComponent`, so on the main menu there
  is nothing to leave room for. Placed by a postfix on
  `Window.SetInitialSizeAndPosition` gated on the instance, this window overriding
  nothing; `Reposition` is the layout toggle saying so by hand - both the shape
  `Patch_MainTabWindowShift` already has.
- The category is **added at startup rather than shipped as XML**. A def survives
  this mod declining to patch (see [profile](profile.md)), and a category whose
  page is never drawn is an empty tab in somebody else's options menu. It is
  Core's def as far as `Dialog_Options` is concerned - the window draws a category
  only if its own `modContentPack.IsOfficialMod` - and first because
  `AllDefsListForReading` *is* the database's list and the column is drawn in its
  order.
- Vanilla lays that window out in **window** coordinates rather than off the rect
  it is handed - the category column is a literal `Rect(0, i*50, 160, 48)` - so
  the centred band inside it is a `GUI` group rather than a remapped rect, for the
  same reason `InspectPaneUtility.DoTabs` is wrapped rather than shifted. Closed
  from a **finalizer**: a postfix does not run when the original throws, and a
  group left open is every window after it drawn somewhere else.
- The rect handed on is the band plus the row vanilla reserves for OK, so the
  options list gets that room and the button is laid out past the bottom of the
  group. The button itself goes the way `StripOptions` takes a row, there being
  nothing for it to dismiss that the corner cross and Escape do not.
- `DoOptions` is a chain of comparisons against vanilla's own eight categories, so
  ours would fall through it and draw nothing; the prefix is taken **ahead** of
  the chain because the page is two columns and its own scroll view rather than
  rows on the `Listing_Standard` opened there. `DoCategoryRow` is prefixed too,
  only for our row: `ContentFinder` knows about files and `TerminalIcon` is drawn
  in code. One pair of prefixes per category of ours, each gated on its own def.
- `Reread` is what keeps two pages off one file: every Save here PUTs whole
  sections, so a page holding a copy read before somebody else's write would put
  the old figures back on its own Save. Both re-read on either save, the one that
  just wrote included.
