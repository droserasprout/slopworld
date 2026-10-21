# Mod source layout

Paths are relative to `mod/Source/SlopWorld/`. Directories group responsibilities,
not assemblies; the SDK project discovers production C# sources recursively.
Handwritten types use `SlopWorld`; generated protobuf types use `SlopWorld.Wire`.

| Area | Responsibility |
| --- | --- |
| `Bootstrap/` | Startup, profile gating, and the RimWorld mod entry point. |
| `Application/` | Features that outlive a map, including [jukebox playback](mod-jukebox.md). |
| `Platform/` | Child processes and native window control. Harmony hooks stay in `Patches/`. |
| `Client/` | Connection and daemon-backed state. `SessionHub/` coordinates services; see [client ownership](mod-client.md). |
| `Settings/`, `Serialization/` | Profile preferences, persistence, and shared format adapters. Callers validate feature schemas. |
| `Sim/`, `Patches/` | Simulation and Harmony integration, respectively. Start with [simulation](mod-sim.md). |
| `UI/Chrome/`, `UI/Theme/` | Shared controls, geometry, styling, and icons. See [chrome](mod-ui-chrome.md). |
| `UI/Display/`, `UI/Cursor/` | Window-wide effects and cursor rendering, independent of terminal caches. |
| `UI/Workspace/`, `UI/Sidebar/` | Panel placement, host chrome, and sidebar navigation. See [workspace](ui-dynamic-layout-architecture.md) and [sidebar](mod-sidebar.md). |
| `UI/Browsing/`, `UI/Readers/`, `UI/Views/` | Shared scopes/trees, reader lifetime, and feature views. See [readers](mod-file-readers.md). |
| `UI/Forms/`, `UI/Dialogs/`, `UI/Async/` | Reusable fields, window lifetimes, and request guards. Callers own validation and transport. See [focus](ui-focus.md) and [dialogs](mod-ui-windows.md). |
| `UI/Terminal/` | Terminal input, rendering, history, and links. Path recognition belongs to `UI/Utilities/`; see [terminal](mod-terminal.md). |
| `UI/Settings/` | Settings pages and drafts. See [apply boundaries](ui-settings.md). |

Game-free source linking belongs to [C# tests](test-csharp.md) and
[IPC benchmarks](../bench/ipc/README.md).
