# Stripping the sim, the UI and the options menu

- **`StripPatches`** - the sim, killed by declining to tick it rather than by
  patching out systems one at a time.
- **`StripUI`, `StripInteraction`** - hiding a main button is not taking its tab
  away: two roads reach the Architect menu and neither looks at `Visible`, so
  `Patch_MainButtons` prefixes `MainButtonWorker.InterfaceTryActivate` and gates
  it on the same `Visible` the bar reads. A button missing from `Keep` never
  appears.
- **`StripKeys`** - the keyboard, cut to the camera, the two that walk the
  colonist bar, `Accept`/`Cancel` and ours; the other fifty-odd vanilla bindings
  drive systems this mod does not run. Dropped means two things. **Unlisted**:
  `KeyBindingsPage` draws `StripKeys.Kept`, and its conflict warning reads the
  same set, a clash named against a row nobody can see being noise. **Unbound**:
  a prefix on the four `KeyBindingDef` read paths (`KeyDownEvent`, `IsDownEvent`,
  `JustPressed`, `IsDown`) answers false. The defs themselves stay in the
  database for `StripOptions`' reason - `AllDefs` *is* the list, and `KeyPrefs`
  keys its table on the def.
  The read is what is answered, rather than each system patched where it sits,
  because two of them are out of reach of hiding anything: `ScreenshotTaker`
  reads its key off `Root.Update` through `JustPressed`, so **no `Event.Use()`
  stops F10** on its way to a TUI; and `KeyBindingDefGenerator` gives every
  `MainButtonDef` with a `defaultHotKey` an implied binding - **Tab and F1
  through F9**, our palette and the sidebar's views - which
  `MainButtonsRoot.MainButtonsOnGUI` **Uses before** `InterfaceTryActivate`,
  where `Patch_MainButtons` turns it away. Hidden button, swallowed key.
  The Controls tab's **"Modify"** button is dropped as a row, vanilla's
  `Dialog_KeyBindings` listing the whole database regardless.
- **Dev mode went with them** - the nine developer bindings are not kept, our
  checkbox on `ConfigPage` is gone, and vanilla's own row stays dropped. No door
  to it inside this game; the unmodded one is where something gets tried.
- **`StripOptions`** - General and Gameplay go by `isDev = true`, the switch `Dialog_Options`
  already reads: it skips a dev category and advances its row counter only for the
  ones it draws, so the column has no hole. Not by removing the def from
  `AllDefsListForReading` - that list *is* the database's own, so a def out of it
  is gone from every `AllDefs` walk while `GetNamed` still answers. Controls loses
  the key bindings "Modify" button by its finished label. With dev mode unreachable
  from inside, `isDev` is now a one-way door. A vanilla options window that defaulted
  to the hidden General category is redirected to SlopWorld General on `PostOpen`.
  Interface loses its **UI scale** row the same way: Appearance has that slider over the
  whole range now (see [mod-ui-windows](mod-ui-windows.md)), and two doors to one pref
  disagreeing about its span is one door too many.

## `SlopOptions` - our categories in vanilla's options window

The SlopWorld pages are inserted as one group ahead of the remaining RimWorld
categories. Daemon and mod settings live there rather than in windows of their own,
so there is one page a knob is looked for on.

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
  only for our row, which wants `Icons.Terminal` rather than a vanilla texture.
  One pair of prefixes per category of ours, each gated on its own def.
- `Reread` is what keeps two pages off one file: every Save here PUTs whole
  sections, so a page holding a copy read before somebody else's write would put
  the old figures back on its own Save. Both re-read on either save, the one that
  just wrote included.
