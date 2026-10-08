# Settings and dialogs

`ModOptions` owns Settings navigation and lazily retained page instances. Directory
owners are mapped in [mod sources](mod-source-layout.md), and persistence belongs
to [Settings](ui-settings.md).

In-game Options uses an embedded content host; the main menu uses a real dialog.
Embedded drawing must clean up GUI groups after exceptions. Dialogs above a terminal
host need Super-layer promotion: `OpenOverPane` applies it, and `ChromeDialogs`
also promotes added dialog-layer windows. [Workspace panels](mod-workspace-panels.md)
own retained content lifetime.

Alerts scroll their message inside a capped viewport with a fixed action footer.
Shared text-dialog notes measure and draw through the same native wrapping path.
Shared form geometry/focus belongs to [chrome](mod-ui-chrome.md) and [focus](ui-focus.md).

`UiWindow` owns resize bounds; resizable forms declare `MinimumSize`.
Native grip integration belongs to `Patches/Chrome/WindowResizeCorner.cs`.

Hidden vanilla category definitions remain registered and layout preserves vanilla
row indexing and lookup identity. [Stripping](mod-patches-strip.md) owns disabling
behavior without deleting definitions.

`RimWorldPage` owns the retained vanilla dialog/settings and build links; `AboutPage`
owns credits. Both embedded and standalone Options closure release pages and save
SlopWorld profile preferences; RimWorld preferences retain their own lifecycle.

Over-pane UI draws after the fullscreen fill in window contents. Screenshot visibility
checks cover map components/extras/overlays separately from the pane. Absorbing windows
can consume input before lower owners; `Use()` alone does not arbitrate overlapping
targets. Gesture owners retain release even outside bounds.

The Sandbox preset list orders single-dependency chains as nested groups within
each User/System section. Multi-dependency bundles stay at the root; missing or
cyclic parents must leave entries visible. `PresetHierarchy` owns this ordering.
