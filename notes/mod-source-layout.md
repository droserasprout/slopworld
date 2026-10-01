# Mod source layout

Source files use the single `SlopWorld` namespace.
Directories describe component responsibilities. They do not create assembly boundaries. The SDK project discovers production C# sources recursively.

- `Bootstrap/` owns startup, profile gating and the RimWorld `Mod` entry point.
- `Client/` is split into `Daemon/`, `Diagnostics/`, `Transport/`, `SessionHub/` and generated protocol, defaults and usage data.
  `Diagnostics/` owns opt-in performance, memory and terminal-latency traces and their frame/file lifecycle.
- `Sim/` is split into `Colony/`, `Content/`, `Jukebox/`, `Lifecycle/`,
  `Plague/`, `Terminal/`, `Worksite/` and `Incidents/`.
- `Patches/` is grouped by `AgentSidebar/`, `Agents/`, `Chrome/`, `ColonistBar/`, `Eco/`,
  `LoadingScreen/`, `MainMenu/` and `Options/`.
- `UI/` keeps existing feature folders and groups shared chrome, terminal code, text helpers,
  usage readouts, utilities and body views under `Chrome/`, `Terminal/`, `Text/`, `Usage/`,
  `Utilities/` and `Views/`.

Use the [shared chrome](mod-ui-chrome.md) helpers for new controls and
[focus lifetimes](ui-focus.md) for editable forms.
See [loading screen](mod-loading-screen.md) for ownership of the loading-time tip stream and
its glyph atlas.
