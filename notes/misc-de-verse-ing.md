# De-Verse-ing scope

Scope for a standalone Unity/C# SlopWorld frontend with the non-game workspace
available permanently. Retain the Rust daemon and its protocol; remove the dependency
on RimWorld, Verse, Harmony integration and game-owned resources. This describes a
possible extraction, not an implemented standalone mode.

Verse is Ludeon's application and game framework above Unity. Replace the application
services SlopWorld consumes; a general-purpose recreation of Verse is outside scope.
Unity remains responsible for rendering, IMGUI primitives, input and its application
lifecycle. Our host would own startup, update dispatch, shutdown and workspace state.

## Current application dependencies

| Boundary | Current use and replacement scope |
| --- | --- |
| Windows and input | Verse windows and `Find.WindowStack` host terminals, dialogs and menus. Supply window ordering, modal behavior, focus, event routing and open/close lifetimes. |
| Menus and tooltips | Our `UiMenu` owns submenu behavior but uses Verse windows, RimWorld menu option types and game UI sounds. Supply independent option data, hosting, tooltips and feedback. |
| Widgets and text | Shared chrome wraps Verse labels, buttons, drawing helpers, text styles and measurement. Supply these services using Unity while retaining our layout, scrolling and control logic. |
| Fonts and scaling | Font setup accesses Verse style arrays and metrics; UI scaling uses game preferences and Verse coordinates. Own styles, font resources, metrics and consistent drawing/input transforms. |
| Lifecycle | Mod bootstrap and Harmony hooks attach our services to game startup and updates. Move the client completion pump and service updates into the standalone host. |
| Preferences and paths | SlopWorld already has TOML settings, but profile paths and some application preferences come from the game. Own frontend storage paths and application preferences; daemon configuration remains daemon-owned. |
| Resources and utilities | Content lookup, fallback textures, UI sounds and logging use game services. Supply resource loading, lifetime management, logging and the small utilities actually required. |

Start with [shared chrome](mod-ui-chrome.md), [workspace ownership](ui-dynamic-layout-architecture.md),
[focus](ui-focus.md) and the [client boundary](mod-client.md). The source owners are
`UI/Chrome/`, `UI/Text/`, `Bootstrap/`, `Settings/` and `Client/`.

## Retained behavior and extraction boundaries

- Keep daemon-owned sessions, terminal emulation, sandboxes, workers, tasks and configuration.
  The C# transport and session stores remain the frontend boundary, with game dependencies removed.
- Retain terminal panels, split layout, history, selection, rendering caches and input behavior.
  `ITerminalPanelHost` provides an existing host boundary; font, menu and window integration
  still require adaptation. See [terminal ownership](mod-terminal.md).
- Retain workspace navigation, Files/Git views, settings, usage and other non-game views.
  Shared helpers reduce the replacement surface, but direct Verse calls also exist in views.
- Make sidebar identity and selection operate on daemon sessions. Remove colonist-bar,
  pawn-selection and portrait-rendering dependencies. See [sidebar integration](mod-sidebar.md).

## Removed systems and resource boundary

Colony simulation, maps, pawns, worksites, plague, game saves, cutscenes and their Harmony
patches are outside the standalone scope. No hidden colony is required to host the UI.
Current [Eco mode](mod-eco.md) still retains maps and pawns, so enabling it permanently
does not remove the game dependency.

Replace game-origin portraits, artwork, sounds, icons and other resources with independently
sourced assets. In particular, [baked backgrounds](mod-background.md) derive from installed
RimWorld artwork; their generated cache is also part of that dependency.

Build and distribution must use a standalone Unity project and independently supplied
dependencies, without resolving assemblies or assets from a RimWorld installation.
