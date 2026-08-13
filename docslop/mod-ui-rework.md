# Next: the flat UI, what is left

Steps 1 to 4 are in. Read [mod-ui-chrome](docslop/mod-ui-chrome.md) first: the
ramp, `Slab`, `Button`/`Bar`, the form controls and the heights are all there.

---

## Done already

1. **The palette.** Eleven greys down to one five-rung ramp on `SlopWidgets`,
   plus the surfaces. Nothing outside `TerminalTheme` mixes its own grey.
2. **`Slab` and `Button`.** Rounded rectangles snapped to the screen grid, a flat
   button in four kinds, and the three list windows migrated.
3. **The rest of the buttons, and the forms.** No `Widgets.ButtonText` or
   `l.ButtonText` left outside the two Harmony patches on vanilla's own; no
   `TextEntry`, `TextArea`, `TextField` or `CheckboxLabeled` outside the command
   palette. `Button`, `Field` and `Area` took an `on`, `Area` a `frame`,
   `Checkbox` a `locked`.
4. **Headings and spacing.** `Header` is `Title` plus a right-aligned status pill;
   `SectionHeading` exists and is used everywhere a group of controls begins.
   Heights off `Text.LineHeightOf`, gaps down to `GapXS`/`GapS`/`GapM`/`GapL`,
   and `PageCaption`/`PageBody`/`FooterBar` for the shape the pages share.

## 5. Loose ends

- **Nothing here has been looked at in the game.** All four steps build; the
  geometry is argued, not seen. The forms grew ~8px a field and the three edit
  dialogs lay out from `l.CurHeight` with fixed things under them - the agent
  dialog was given 40px for it, the other two were not measured.
- **`CommandPalette` never went through any of this.** It mixes its own greys
  (0.10/0.11/0.13, 0.35 black, 0.45 grey, a 0.28/0.40/0.60 selection) and its two
  `Widgets.TextField`s still wear vanilla's textured box over its own well. It
  drives its own focus by name, so `Field` needs the palette's name passed in, or
  a bare variant. One file, its own commit.
- **`RowChrome`** fills at 3% white, invisible at a 52px row. Try zebra (0%/3%)
  plus a 2px left accent bar in `TerminalWindow.StateColor` on hover.
- **A `Chip(rect, text, color)`** - `TopBar` (~93) draws a state bar by hand, the
  agent rows draw their own, and `SessionsWindow.DrawRow` draws a third. One
  helper, so state reads the same in all three. `Header`'s status pill is nearly
  it already.
- **Radius** is 5 for everything. If the 22px row buttons still read pill-ish next
  to the 30px footer ones, `Slab.R` is the one number.
- **Accent** is `PrimeFace = 0.15, 0.33, 0.57`, tuned twice by eye. If the mod ever
  wants a real accent color of its own, that is the line, and `FocusEdge` should
  move with it.

## Traps

- **UI scale is 1.75 here.** Anything new that draws a shape goes through
  `Slab`, or through its own `Snap` - not `Mathf.Round` on GUI coordinates, and
  never `ScreenToGUIPoint`. A hairline goes through `Slab.Hairline`.
- `Text` is `Verse.Text`. The bright rung is `Lead` for that reason.
- `Listing_Standard` breaks to a second column the moment a control would cross the
  bottom of the rect it was begun on. `maxOneColumn = true` and a tall rect - see
  `EditProjectDialog.DoFields`.
- Two boxes sharing a `Field` name share a focus. Names are `form.field` here.
