# Refactor: remaining mod god classes

Classes flagged by `tokensave_god_class` that aren't yet split. `AgentSidebar`
and `TerminalWindow` already use the partial-file pattern
(`.Rendering.cs`, `.State.cs`, `.Input.cs`) — apply the same to these.

- [x] `UI/CommandPalette.cs` — `CommandPalette`, 79 members (40 methods).
      `HandleNavigation` alone is 79 lines. Split input/navigation from
      rendering into partial files as the sidebar does.
- [x] `Client/SessionHub.cs` — `SessionHub`, 66 members (**50 methods**). The
      per-event `Handle` (58 lines, 9 branches) and `FromJson` (33 lines,
      fan-out 33) suggest event dispatch and JSON mapping should each move to
      their own partial/file.
- [ ] `Sim/Worksite.cs` (89) and `Sim/Plague.cs` (80) — large but cohesive sim
      classes; lower priority. Only split if a clear seam appears (e.g. state
      vs. reconcile). Don't split just to hit a member count.

Guidance: partial files, not new types, unless a genuine responsibility seam
exists — the goal is readable files, not more indirection.
