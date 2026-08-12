# Stripping the sim, the UI and the options menu

- **`StripPatches`** stops the simulation by declining its ticks instead of removing
  systems one at a time.
- **`StripUI`/`StripInteraction`** hide main buttons and gate
  `MainButtonWorker.InterfaceTryActivate` on the same `Visible` value. A button not in
  `Keep` is neither drawn nor activatable; hiding the button alone leaves the Architect
  menu reachable by another path.
- **`StripKeys`** keeps only camera, colonist-bar navigation, `Accept`/`Cancel` and mod
  bindings. It removes dropped bindings from the UI (`KeyBindingsPage` and conflicts)
  and answers false on all four `KeyBindingDef` read paths (`KeyDownEvent`,
  `IsDownEvent`, `JustPressed`, `IsDown`). The defs remain in `AllDefs` because
  `KeyPrefs` indexes them there.

  Answering the shared reads covers paths hiding cannot: `ScreenshotTaker` reads F10
  through `JustPressed` before a TUI can use the event, and
  `KeyBindingDefGenerator` gives `MainButtonDef`s implied bindings (Tab/F1-F9) that
  `MainButtonsRoot` consumes before `InterfaceTryActivate`. The Controls "Modify" row
  is also removed; vanilla otherwise lists the entire database.
- **Dev mode** is not kept: its nine bindings, the ConfigPage checkbox and vanilla row
  are gone. Use the unmodded game for developer tools.
- **`StripOptions`** skips General and Gameplay with `isDev = true`, while leaving defs
  in the database so `GetNamed` and `AllDefs` stay coherent. It removes Controls'
  finished "Modify" row and redirects a vanilla window opened on hidden General to
  SlopWorld General. Appearance owns the full-range UI-scale slider, so vanilla's row
  is removed too ([mod-ui-windows](mod-ui-windows.md)).

## `SlopOptions` - our categories in vanilla's options window

SlopWorld pages are inserted as one group before the remaining RimWorld categories;
daemon and mod settings therefore have one home.

- The window is drawn inside the chrome at `SlopLayout.LeftInset`/`TopInset`. The inset
  is omitted without a colony because the sidebar/top bar have no map-layer owner.
  Placement is a `Window.SetInitialSizeAndPosition` postfix for this instance;
  `Reposition` is the explicit layout toggle.
- Categories are added at startup, not XML. The def survives when this mod refuses to
  patch, is first in `AllDefsListForReading`, and is treated as official by
  `Dialog_Options` so the page is drawn.
- Vanilla uses window coordinates and literal category rects, so the centred content is
  a `GUI` group rather than a remapped rect. A finalizer closes the group even if the
  original throws.
- The passed rect includes vanilla's reserved OK row; the button is laid out below the
  group and its action is stripped because the corner cross/Escape already close the window.
- `DoOptions` and `DoCategoryRow` are prefixed for each SlopWorld category. Our pages
  use their own two-column scroll view and an `Icons.Terminal` row icon rather than
  vanilla's `Listing_Standard` chain.
- `Reread` prevents lost settings: saves write whole sections, so both pages reload
  after every save, including the page that just wrote.
