# Next: the flat UI, steps 3 and 4

Steps 1 and 2 are in. What they left behind is below, in the order it should be
done - each step is a commit, and the rebuild after each is the point.

Read [mod-ui-chrome](docslop/mod-ui-chrome.md) first: the ramp, `Slab`, and
`Button`/`Bar` are all described there.

---

## Done already

1. **The palette.** Eleven greys across four files down to one five-rung ramp on
   `SlopWidgets` - `Lead`, `Name`, `Dim`, `Faint`, `Off` - plus `Panel`, `Edge`,
   `EdgeLit`, `Well`, `RowBg`, `RowOn`. Nothing outside `TerminalTheme` mixes its
   own grey any more.
2. **`Slab` and `Button`.** Rounded rectangles snapped to the screen grid, and a
   flat button in four kinds. Migrated: the three list windows (Projects, Agents,
   Shortcuts) - footers through `Bar`, row buttons at `RowBtnH`.

## 3. The rest of the buttons, and the forms

**20 `Widgets.ButtonText` left.** `ConfigPage` (5), `EditSessionDialog` (5),
`UsagePage` (3), `SandboxPage` (2), `ConfigWindow` (2), `AboutPage` (1),
`SlopOptions` (1), `InspectPanePatch` (1). Same swap as the list windows: footers
become a `Bar`, Save becomes `Primary`, anything that removes becomes `Danger`,
Reload/Cancel become `Ghost`.

**Wire up `Field`, `Area` and `Checkbox`.** They are written and unused. The
catch: most of these forms are `Listing_Standard`, whose `TextEntry` and
`CheckboxLabeled` draw vanilla's own chrome. Either take the rect with
`l.GetRect(h)` and draw ours into it, or stop using the listing for those rows.
Prefer the first - the listing's column and gap handling is worth keeping.

`SlopWidgets.PathList` should end up as a label plus `Area`.

Watch for: `l.ButtonText` in `ShortcutsWindow` (3 of them, lines ~209-228) is the
listing's own and needs `l.GetRect` + `SlopWidgets.Button`.

## 4. Headings and spacing

**`SlopWidgets.Header`** measures the title with `Text.CalcSize` and drops the
daemon status inline at `x + w + 16`. Replace with: title left, status as a
dot-and-text pill **right-aligned**, hairline rule (`Edge`) under both. Right
alignment removes the measuring entirely.

**`SectionHeading(rect, text)`** - there is no such thing today, so form sections
are body text in body colour (`l.Label("Sandbox presets")`, `ProjectsWindow` ~234;
`l.Label("Command presets...")`, `ConfigPage` ~94). Draw in `Faint`, small, with a
hairline to its right. This is most of what makes `ConfigPage.DoFields` read as a
wall.

**A spacing scale.** Live gaps across those files: 2, 4, 6, 8, 10, 12, 16, 18, 20,
22, 24, 26, 28, 30, 32, 34, 40, 52. Go to **4 / 8 / 16 / 24** with named heights
(`BtnH` and `RowBtnH` exist; add `RowH`, `HeaderH`).

**Derive text rects from the font**, never a literal - `Text.LineHeightOf`, the way
`AgentSidebar` (~61) already does. A figure eyeballed against one font crops
descenders on every other, which has cost the labels their bottom row once.

## 5. Loose ends

- **`RowChrome`** fills at 3% white, invisible at a 52px row. Try zebra (0%/3%)
  plus a 2px left accent bar in `TerminalWindow.StateColor` on hover.
- **A `Chip(rect, text, color)`** - `TopBar` (~93) draws a state bar by hand and the
  agent rows draw their own. One helper, so state reads the same in both.
- **Radius** is 5 for everything. If the 22px row buttons still read pill-ish next
  to the 30px footer ones, `Slab.R` is the one number.
- **Accent** is `PrimeFace = 0.15, 0.33, 0.57`, tuned twice by eye. If the mod ever
  wants a real accent colour of its own, that is the line, and `FocusEdge` should
  move with it.

## Traps

- **UI scale is 1.75 here.** Anything new that draws a shape goes through
  `Slab`, or through its own `Snap` - not `Mathf.Round` on GUI coordinates, and
  never `ScreenToGUIPoint`.
- `Text` is `Verse.Text`. The bright rung is `Lead` for that reason.
- `Listing_Standard` breaks to a second column the moment a control would cross the
  bottom of the rect it was begun on. `maxOneColumn = true` and a tall rect - see
  `EditProjectDialog.DoFields`.
