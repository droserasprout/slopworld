# Smells review plan

This is a prioritized cleanup plan for correctness risks and ownership smells across `slopd`
and the RimWorld client. Make each cleanup a small behavior-preserving change.

## Goals

- Make provider and session failures explicit instead of silently degrading.
- Keep runtime state, persisted state, and UI tab state behind clear ownership
  boundaries.
- Preserve wire behavior, event ordering, input consumption, and terminal
  lifecycle semantics while refactoring.

## Plan

1. **Make provider failure explicit.** Convert blocking-task join errors to failed snapshots that
   contain the join error. Add coverage for panic/cancellation and aggregate usage state.
2. **Make errand reservation transactional.** Rework
   `create_errand_session` so a live-name reservation and persistent host
   update cannot leave one side committed after the other fails. Prefer one
   serialized reservation boundary; otherwise add a compensating rollback and
   concurrency tests.
3. **Unify preview ownership.** Extract a shared lifecycle model between `PagerTabs` and
   `FilesView`'s Markdown preview handling for identity, replacement, locking, reopening, routing,
   and close cleanup, while leaving daemon sessions and native Markdown content as separate
   adapters.
4. **Decouple terminal close cleanup.** Replace the hard-coded fan-out from
   `TerminalWindow` to every viewer owner with a narrow close notification or
   registered owner interface. Preserve cleanup order and make adding a new
   viewer local to that viewer.
5. **Bound sidebar click state.** Reset the routed double-click sequence when a mouse-down is not
   the same routed target, including ordinary tree rows and empty space. Keep single-click
   selection, right-click menus, and double-click pinning unchanged; cover the pure click
   sequence behavior.
6. **Shorten async lock and recovery paths.** Snapshot live names and config before validation in
   `mint_grant`. Split orphan adoption into probe, decision, and commit stages, and use bounded
   concurrency only for independent external probes. Keep state mutation and reader attachment
   ordered.
7. **Remove duplicated UI layout logic.** Share the metric, truncation, and
   tooltip layout path between normal and italic row labels so visual changes
   cannot drift between overloads.

## Guardrails

- Extract one stable concept at a time; do not refactor by line count alone.
- Keep lifecycle, state-transition, and input-ordering tests beside their owner.
- Run `make format`, `make test`, and `make lint` after each behavior-preserving
  stage. Use `git diff --check` before handoff.
- Do not run the game or take screenshots as part of this plan.
- Refresh this note and the [maintainability-hotspots-plan](maintainability-hotspots-plan.md)
  when ownership boundaries or priorities materially change.
