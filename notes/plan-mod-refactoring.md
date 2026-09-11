# Mod complexity and LoC reduction

Status: planned; static review only. Implement in the order below, one reviewable
change at a time. Scope is C# mod code; preserve features, UI behavior, persistence,
wire requests, and timing. Do not launch the game or inspect images unless asked.

Related: [terminal](mod-terminal.md), [terminal rendering](mod-terminal-rendering.md),
[settings](ui-settings.md), [C# tests](test-csharp.md).
Source paths below are relative to `mod/Source/SlopWorld/`.

## 1. Remove redundant terminal state accessors

Files: `UI/Terminal/TerminalPanel/TerminalPanel.{Selection,Runtime,Sizing,State}.cs`
and their callers.

- Inventory forwarding properties and callers. Selection currently exposes some
  fields through both internal properties and private aliases; runtime and sizing
  also forward into the panel-owned state.
- Use the owned state directly within panel partials. Keep only the narrow surface
  needed by input controllers; do not expose the full state merely to save lines.
- Remove unused aliases after migrating callers. Preserve field defaults, access
  semantics, per-panel ownership, and controller/host boundaries. Keep the pure
  `TerminalSelectionState` and its behavior helpers.
- This step is mechanical; do not move rendering or history algorithms between
  classes or merge coordinators as part of it.

## 2. Use one listing per settings form

Files: `UI/Settings/AppearancePage.cs`, `UI/Settings/TerminalPage.cs`.

- Change section methods to accept the form's `Listing_Standard`; begin/end once
  around the complete form, with `End` in `finally`. Remove each section's listing
  construction, height return, and the caller's accumulated rectangle arithmetic.
- Preserve section gaps, control IDs/order, one-column behavior, final padding,
  preview placement, scrollbar reservation, and settings invalidation callbacks.
- Retain the current outer scroll/preview hosts. Do not add nested scrolling or
  force both pages into a new base class. Preserve frame-stable height publication;
  reuse `SettingsContentHeight` in Appearance only if its initialization and
  measurement behavior remain equivalent.

## 3. Consolidate common terminal history reset assignments

File: `UI/Terminal/TerminalPanel/TerminalPanel.HistoryCoordinator.cs`.

- Compare `ResetForNewRun` and `ResetForSession` field by field, including assignments
  deliberately absent from one path. Extract only identical common reset work.
- Keep the new-run guard, live-sequence reset, cache eviction, jump request, and
  selection clearing explicit in that path. Keep session-switch wheel/scroll
  initialization and subsequent cache restoration explicit in the other.
- Preserve callback order, request invalidation, run/connection identity checks,
  fractional offsets, and cached frames. Do not replace the two entry points with
  a boolean-driven reset API or broaden this into a history redesign.

## 4. Share the project/session DNS editor

Files: `UI/Views/Projects/ProjectsView.cs`,
`UI/Dialogs/EditSessionDialog{,.Tabs}.cs`, `Client/SessionHub/DnsConfig.cs`.

- Extract a small DNS form helper for resolver choices, custom-server text, and
  common hints. Session editing supplies the optional inherit choice and label;
  project editing always owns an explicit DNS value.
- Centralize save-time text parsing through the existing `TryParseServers`; keep
  invalid raw input editable until Save and retain the current error text.
- Preserve null-as-inherit, copy isolation, current mode-switch behavior, field
  IDs, and request payloads. Leave network controls and dialog shells alone.

## 5. Evaluate Library group-rendering reuse

Files: `UI/Views/Library/LibraryView.cs`, `UI/Views/Shared/ContentTreeView.cs`,
`UI/Views/Shared/ContentTreeController.cs`.

- Map Library's headings, scroll geometry, folds, measurement, and hit testing to
  the existing tree contracts before editing. Preserve ordering, the loose bucket,
  badges, selected row, breadcrumb gating, menus, and single-click run behavior.
- Proceed only if an adapter or small shared helper removes more code than it
  adds. Avoid fake directory semantics, broad new capability interfaces, or
  Library-specific branches throughout the shared tree. If those are necessary,
  record the finding and leave this candidate deferred.
- Keep Files/Git/Search asynchronous lifecycles separate; their concurrency and
  stale-response rules are not interchangeable.

## Validation and completion

- Run `make test-mod` and `make lint-mod` before implementation to establish the
  baseline, then after each change. Use `make format-mod` as needed. Report missing
  game assemblies/tooling or pre-existing failures separately from regressions.
- Reuse selection, terminal history, settings layout/height, DNS, and content-tree
  tests. Add focused behavior tests only where changed logic warrants them; no
  source-text assertions, duplicate implementations, or game-widget mock framework.
- For history resets, verify both resulting states and intentional differences.
  Existing pure history tests do not exercise the game-bound coordinator; report
  that coverage gap unless a small production seam supports a meaningful test.
- Capture before/after LoC with `make loc-report`; distinguish production-code
  reductions from comments, tests, and unrelated workspace changes. Each accepted
  step must reduce net production code and remove duplication or indirection.
- Review UI geometry/input invariants statically. Record in-game validation as
  unperformed; compilation and pure tests do not establish visual equivalence.
- Update focused ownership notes if needed. Delete this plan once accepted steps
  are implemented and any deferred candidate has a recorded disposition.
