# Plan: simplify settings state and target definitions

Status: implemented. Two independent small refactors; no new features.

Completed: preview requests now use `AsyncLoadState<string>`, and summary targets are
ordered definitions with config-parameterized accessors. Actual source diff: 34 additions,
75 deletions, net -41 lines (`InstructionsPage` -17; `SummariesPage` -24).

## Instructions preview request state

`UI/Settings/InstructionsPage.cs` repeats generation, busy, error, and result
bookkeeping already provided by `UI/Views/Shared/AsyncLoadState.cs`.

1. Replace preview request fields with `AsyncLoadState<string>`. Keep project
   selection and the request payload in `InstructionsPage`.
2. Use the successful-load callback to call `MarkdownPreview.SetInlineText`.
   Render busy, error, and empty states in their current precedence; use
   `HasValue` so a failed refresh cannot display an older successful result.
3. Invalidate on disposal before closing the Markdown preview. Preserve config
   load/save behavior, feature gates, and the timing of preview requests.
4. Verify overlapping requests, failure after success, and callbacks arriving
   after disposal with existing game-free async-state tests; add missing cases
   only where they test a real lifecycle guarantee.

Estimated net reduction: 15–25 lines. Do not extend this change to search's
multi-request aggregation or Markdown parsing state.

## Summary target definitions

`UI/Settings/SummariesPage.cs` spreads three targets across an enum, array,
label/getter/setter switches, and a menu restriction.

1. Replace these mappings with a small ordered target definition containing its
   label, config getter/setter, and whether it permits the Always policy.
2. Pass the current config to accessors rather than capturing a draft that can
   become stale after Reload. Reuse definitions in wide and stacked rendering.
3. Keep target order, labels, policy fallback, and the task-only Never/Once menu
   unchanged. Retain `PolicyLabel` if other callers use it.

Estimated net reduction: 20–35 lines. Avoid a general settings-schema framework.

## Acceptance and verification

Implement after the [facade migration](mod-ui-facade-plan.md), preferably as two
small changes. Run `make lint-mod` and `make test-mod` for each implementation;
record actual net source-line changes. Do not run the game or inspect screenshots
without an explicit request. Update focused notes only if ownership facts change.

Validation: `make lint-mod` and `make test-mod` pass; the latter reports 225 tests.
