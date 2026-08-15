# Refactor: long linear methods

Long sequential procedures — not deeply tangled (low nesting), so extract-method
is the whole job. Worst first.

`slopd/src/manager/lifecycle.rs`:

- [x] `start` (L431, 92 lines, cyclomatic 17). Sequential pipeline: resolve
      session → resolve project → network check → dir checks (create/is_dir/
      sandbox `refused`) → build argv → spawn → wire live title/breadcrumbs.
      Extract `resolve_target`, `validate_dir`, `wire_live_state`.
- [x] `file_action` (L842, 90 lines) and `run_errand` (L949, 84 lines,
      cyclomatic 13) — same treatment, pull each phase into a named helper.

Other files:

- [x] `mod/.../Patches/SessionSelectable.cs::GetGizmos` (L67, **99 lines** —
      longest method in the repo). Each gizmo is an independent block; extract
      one builder per gizmo.
- [x] `slopd/src/api.rs::search` (L1413, 111 lines) — lower priority, mostly
      linear rg-argv assembly + JSON parse loop. Extract the argv-building block
      (`build_rg_command`) if touched.

These are readability wins, not correctness — do them opportunistically when
already editing the function.
