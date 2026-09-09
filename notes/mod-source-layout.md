# Mod source layout

Source files stay in the single `SlopWorld` namespace; directories describe ownership and
do not create assembly boundaries. The SDK project discovers the mod recursively.

- `Bootstrap/` owns startup, profile gating and the RimWorld `Mod` entry point.
- `Client/` is split into `Daemon/`, `Transport/`, `SessionHub/` and generated wire data.
- `Sim/` is split into `Colony/`, `Content/`, `EasterEggs/`, `Jukebox/`, `Lifecycle/`,
  `Plague/`, `Terminal/` and `Worksite/`.
- `Patches/` is grouped by `AgentSidebar/`, `Agents/`, `Chrome/`, `ColonistBar/`, `Eco/`,
  `LoadingScreen/`, `MainMenu/` and `Options/`.
- `UI/` keeps existing feature folders and groups shared chrome, terminal code, text helpers,
  usage readouts, utilities and body views under `Chrome/`, `Terminal/`, `Text/`, `Usage/`,
  `Utilities/` and `Views/`.
