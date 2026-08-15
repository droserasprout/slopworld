# Refactor: TerminalWindow partials

Owns: `mod/Source/SlopWorld/UI/TerminalWindow.cs` and `UI/TerminalWindow/`.
See [mod-terminal](mod-terminal.md).

`TerminalWindow` is 907 lines / 50 methods. It already has `.Input` and
`.Rendering` partials under `UI/TerminalWindow/` — the pattern works, it just has
not gone far enough. `DoWindowContents` (90 lines, cognitive 23) still lives in
the root file.

Steps:

- Move state/lifecycle methods (open/close, session bind, config mirror, resize)
  from `TerminalWindow.cs` into a new
  `UI/TerminalWindow/TerminalWindow.State.cs` partial.
- Break `DoWindowContents` into the sections it already draws (top bar / body /
  status), each a private method — `DoWindowContents` becomes an orchestrator.
- `HandleMouse` (in `.Input`, 93 lines, cognitive 39, **11 return points**):
  extract per-region hit tests into named predicates to flatten the branching.

Done when: `TerminalWindow.cs` holds construction + `DoWindowContents`
orchestration only, and no method there runs past ~40 lines.
