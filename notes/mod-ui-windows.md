# Settings and dialogs

`ModOptions` registers the Settings tree and caches page instances. Read its definitions
for current navigation.
`UI/Settings/` and `UI/Dialogs/` contain forms. Avoid duplicating the
page/control inventory here. [Apply behavior](ui-settings.md) owns persistence boundaries.

Options renders inside the fullscreen content host.
Ordinary dialogs appear above it through `TerminalWindow.OpenOverPane`. Vanilla dialogs opened by a page need the same Super-layer
promotion or the terminal paints over them. See [content views](mod-content-views.md).

Shared form geometry and scroll lifetimes must preserve drafts/focus when rearranging for
small viewports. Measure and draw the same field sequence, but measurement must not invoke
controls or setters. Keep footers and overlays with their feature owner.

Vanilla category definitions remain in the database even when hidden. Patches translate
base game fixed row positions into the mod layout.
Deleting defs or patching only drawing leaves lookup and shortcut behavior inconsistent. See [stripping](mod-patches-strip.md).
