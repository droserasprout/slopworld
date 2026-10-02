# Shared UI chrome

`UI/Chrome/` owns shared controls and layout helpers. `UiTheme` and styling/icon
catalogs live in `UI/Theme/`; workspace host chrome lives in `UI/Workspace/`.
Start with `UiText`, `UiButtons`, `UiControls`, `UiLayout`, and `Slab` when adding a control.
`TextEntryController` owns shared native field invocation and delayed clipboard edits;
focus memory belongs to [focus](ui-focus.md).

Measurement, drawing, and hit testing must share stable geometry and control IDs
across IMGUI passes. Measurement must not mutate form data. Cache layout by content
and text metrics, not color alone; use shared measurement helpers. `Slab` geometry
is flat and pixel-snapped. Hover must not alter bounds. Spacing and text metrics
belong to shared chrome rather than individual themes.

Chrome colors resolve on read. A texture that bakes them must invalidate on theme
changes. [Theme identity](mod-ui-identity.md) owns catalog contracts. Other focused
owners are [workspace layout](ui-dynamic-layout-architecture.md),
[focus](ui-focus.md), [dialogs](mod-ui-windows.md), [Settings](ui-settings.md),
[text](mod-ui-text.md), and [scrolling](mod-ui-scrolling.md).

`UiMenu` owns menu chains. `Patch_UiMenuWindowStack` closes the chain when a
non-menu window is clicked, before the base game raises that window. Window-stack
and submenu traps belong to [Harmony gotchas](core-gotchas.md).

Responsive Settings forms pin Interface/Terminal/Code previews below the scrolling
form when space permits. Short windows scroll form and preview together; Code
retains a separate save footer. Measurement must not invoke controls/setters.

`Window.Margin` translates the GUI group rather than adding padding. Shared hosts
use explicit body padding. `GameFont.Tiny` may draw as Small, so measurement uses
shared metrics. Use an explicit one-column listing rather than relying on a short
vanilla listing rectangle. Runtime assets needing survival across map changes use
`DontUnloadUnusedAsset`; this is not a rule for every generated texture.

Menu levels need separate instances because vanilla same-type replacement can remove
a standing menu before opening another. Submenu openers need a non-null action,
since vanilla infers Disabled from a null action.
