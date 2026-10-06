# Shared UI chrome

`UI/Chrome/` owns shared controls and layout helpers. `UiTheme` and styling/icon
catalogs live in `UI/Theme/`; workspace host chrome lives in `UI/Workspace/`.
Start with `UiText`, `UiButtons`, `UiControls`, `UiLayout`, and `Slab` when adding a control.
`TextEntryController` owns shared native field invocation and delayed clipboard edits;
focus memory belongs to [focus](ui-focus.md). Clipboard access uses `IUiClipboard`;
bootstrap installs the daemon adapter, which owns capability checks and native fallback.
Providers deliver reads on the UI thread; fields retain delayed-edit lifetime checks.

Shared headers receive status text and availability; list views receive their empty
message and header/footer drawing from the feature owner. Session action classification
belongs to `SessionRowAction`, and agent color mapping to `AgentPresentation`.
Bootstrap supplies `RowChrome` overlay hit testing from the map host. Scene and
screenshot visibility belong to `WorkspaceVisibility`, outside generic layout helpers.

Multiline `Area` controls opt into vertical resizing with a form-owned `UiAreaResize`.
Listing areas measure their height automatically; rectangle callers use `UiText.AreaHeight`
in their layout. Prompts retain a manual height. Template descriptions and preset
areas opt into content growth, capped at 240 pixels. Dragging overrides growth;
the grip's right-click Fit to content action restores it. Read-only preset areas
grow without a grip. Sizing-enabled fields own an inner scroll viewport and reveal
the caret after keyboard edits; full-dialog editors keep their window-owned sizing.
Measurement is pure and includes the same padding and scrollbar gutter as drawing.
The adapter reuses the native window resizer and compact corner grip, reserves text space above the grip, and retires input capture with the field lifetime.
The native grip includes a Unity button that takes mouse capture. The area adapter
restores its own capture and invokes the native grip once after the text control on
every enabled IMGUI pass, keeping control allocation consistent while idle and dragging.

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

`CatalogActions` shares agent/project actions across the palette and sidebar menus.
`LibraryActions` owns library execution, project selection, and management actions;
calling surfaces retain availability rules and navigation history.
