# Refactor plan

Eight isolated units. Each note **owns a disjoint set of source files**, so two
units can be worked at once without touching the same file. Metrics from
`tokensave` on 2026-08-15.

| Unit | Note | Owns (source files) |
|---|---|---|
| A | [tmp-refactor-lifecycle](tmp-refactor-lifecycle.md) | `slopd/src/manager/lifecycle.rs` |
| B | [tmp-refactor-usage-poll](tmp-refactor-usage-poll.md) | `slopd/src/usage.rs` |
| C | [tmp-refactor-worksite](tmp-refactor-worksite.md) | `mod/…/Sim/Worksite.cs` (+ new `Sim/` siblings) |
| D | [tmp-refactor-menu-background](tmp-refactor-menu-background.md) | `mod/…/UI/MenuBackground.cs` (+ new `UI/` siblings) |
| E | [tmp-refactor-terminal-window](tmp-refactor-terminal-window.md) | `mod/…/UI/TerminalWindow.cs` + `UI/TerminalWindow/` |
| F | [tmp-refactor-shortcuts-view](tmp-refactor-shortcuts-view.md) | `mod/…/UI/ShortcutsView.cs` |
| G | [tmp-refactor-sandbox-preview](tmp-refactor-sandbox-preview.md) | `mod/…/UI/SandboxPreviewPanel.cs` |
| H | [tmp-refactor-autosaver-cycle](tmp-refactor-autosaver-cycle.md) | `mod/…/Sim/AutoSaver.cs` + `Sim/QuitInterceptor.cs` |

Context for all units:

- This is a **concentration** problem, not copy-paste. The redundancy scan found
  one duplicate pair only (two tests in `session.rs`). Health signal 6420/10000;
  the two weak dimensions are equality (Gini 0.715, "god files likely") and
  acyclicity — both are the units above.
- **Do not** chase the 273 "dead" functions health reports. Most are Harmony
  `Prefix`/`Postfix` and Verse overrides with no static caller. A dead-code sweep
  is only safe on `slopd`, after filtering those out — not scoped as a unit here.
- Every unit is a behaviour-preserving split. No feature changes.
