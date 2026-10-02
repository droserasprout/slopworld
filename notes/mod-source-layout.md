# Mod source layout

Source files use the single `SlopWorld` namespace.
Directories describe component responsibilities. They do not create assembly boundaries. The SDK project discovers production C# sources recursively.

- `Bootstrap/` owns startup, profile gating and the RimWorld `Mod` entry point.
- `Client/SessionHub/` owns connection coordination and cross-service event handoffs.
  Services and their models live together in `Sessions/`, `Catalog/`, `Tasks/`,
  `Terminal/`, `Usage/` and `Audio/`. HTTP, WebSocket framing and generated bindings
  stay in `Daemon/`, `Transport/` and `Generated/`.
  `Diagnostics/` owns opt-in performance, memory and terminal-latency traces and their frame/file lifecycle.
- `Serialization/` owns shared format adapters; callers own feature-specific schema validation.
- `Sim/` is split into `Colony/`, `Content/`, `Jukebox/`, `Lifecycle/`,
  `Plague/`, `Terminal/`, `Worksite/` and `Incidents/`.
- `Patches/` is grouped by `AgentSidebar/`, `Agents/`, `Chrome/`, `ColonistBar/`, `Colony/`, `Eco/`,
  `LoadingScreen/`, `MainMenu/`, `Options/`, `Plague/` and `Worksite/`.
  Harmony integration stays in `Patches/`; simulation components stay in `Sim/`.
- `UI/Chrome/` owns shared controls. `Scrolling/` owns scroll geometry and input,
  with native sampling in `Scrolling/Platform/`; `Theme/` owns styling and icon catalogs.
- `UI/Workspace/` owns panel contracts, placement and host chrome; `Sidebar/` owns
  sidebar layout, rendering and navigation. Sidebar Harmony hooks stay in `Patches/AgentSidebar/`.
- `UI/Browsing/` owns shared scope catalogs and content trees; `Readers/` owns reader
  sessions and tabs. Feature body views and their editors live under `Views/`.
- `UI/Forms/` owns reusable editor fields and previews; `Async/` owns request guards
  and queues. `Agents/` owns agent editors, while `Dialogs/` keeps generic dialogs.
- `UI/Terminal/` groups input, rendering, history and links by responsibility.
  Each panel/window entry file lives beside its companion partials.
  Text helpers, usage readouts and utilities stay in `Text/`, `Usage/` and `Utilities/`.

Use the [shared chrome](mod-ui-chrome.md) helpers for new controls and
[focus lifetimes](ui-focus.md) for editable forms.
See [loading screen](mod-loading-screen.md) for ownership of the loading-time tip stream and
its glyph atlas.

`UI/Views/Projects/ProjectSummary.cs` owns project summary text shared by sidebar,
Library and edit forms. Project editing belongs to `UI/Views/Projects/EditProjectDialog.cs`.
Library keeps rendering, category/worktree data and interaction in feature-owned partials.
Worktree refresh failures retain the last successful rows and expose a retry in the view.

Settings owns `IOptionPage`. Shared row visibility geometry lives in `UI/Scrolling/`;
terminal path recognition and lexical path resolution live in `UI/Utilities/`.

Usage rows follow daemon snapshot polling and rank. Usage icon policy owns reservations
and fallback assignment: reserve chosen and built-in icons, assign new keys in ordinal
order with polled rows first, and retain existing assignments across replacement snapshots.

Production source discovery is recursive, but the game-free test project and IPC
benchmark explicitly link source paths. Update those links and tool manifests when moving files.
