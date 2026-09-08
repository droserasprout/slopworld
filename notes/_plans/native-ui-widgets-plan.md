# Native UI widgets plan

Baseline: source audit on 2026-09-08. SlopWorld already owns the visible button,
checkbox, selector, slider and field chrome through `UiControls`, `UiLayout`,
`Slab` and `UiText`. Native IMGUI controls remain underneath those helpers for
text editing and invisible hit testing.

## Remaining native surfaces

- Direct `Widgets.Label` calls remain in empty states, errors, captions, dialogs,
  previews and status text. The specialized Markdown, terminal, selection,
  tooltip and loading-screen paths should stay separate.
- `Widgets.ButtonInvisible` is used as the hit adapter in custom controls,
  command-palette rows, history rows, pickers and settings. It does not draw
  native button chrome, but it still carries RimWorld's event semantics.
- `Widgets.ThingIcon` remains in the top bar and usage settings/readouts.
- `TexButton.Reveal`/`Collapse` remains in selectors, menus, trees, Library,
  KeyBindings and sidebar fold controls.
- `Listing_Standard` and `Dialog_Options` remain intentional layout/API hosts;
  their form controls are routed through the shared helpers or patched options
  pages.

## Plan

1. Add one shared wrapped/status-label helper with scheme color, font and anchor
   inputs, then migrate ordinary empty, error, caption and preview text to it.
   Keep `RowLabel` for single-line rows and leave custom Markdown/terminal text
   rendering in its own path.
2. Keep `ButtonInvisible` behind shared control or row helpers where practical.
   Do not change input ordering or event consumption in the sidebar, palette,
   menus, or scroll views without focused verification.
3. Decide which ThingDef icons and disclosure arrows need scheme-owned assets.
   Replace only the surfaces that have a clear SlopWorld equivalent; native
   ThingDef imagery may remain where it communicates a RimWorld object.
4. Preserve native framework boundaries: text editing, `Listing_Standard`,
   `Dialog_Options`, tooltip placement, and vanilla-patch compatibility are not
   migration targets by themselves.
5. After each migration, run `make format`, `make test`, and `make lint`, then
   repeat the source audit and update this note if the boundary changes.
