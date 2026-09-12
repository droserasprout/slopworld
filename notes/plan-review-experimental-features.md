# Breadcrumbs and Instructions implementation plan

Related: [settings behavior](ui-settings.md), [C# tests](test-csharp.md).

## Problems

- Manual delivery in `slopd/src/manager/capture_input.rs::paste_breadcrumb` does not
  check `experimental_breadcrumbs`. Reject disabled delivery at the manager boundary.
- Queued discovery survives disabling Instructions or global/per-agent discovery.
  `slopd/src/manager/reconcile.rs` and input consumption check only Breadcrumbs.
  Track provenance and cancel ineligible discovery without discarding ordinary breadcrumbs.
- Empty project selection in `slopd/src/api/handlers_config.rs::instructions_preview`
  selects the first configured project. Make the UI's sample selection explicitly synthetic.
- `UI/Settings/WorkersPage.cs` disables prompt editing behind Instructions, while
  `slopd/src/manager/workers.rs` submits that prompt unconditionally. Unlock editing.
- `UI/Dialogs/EditSessionDialog.Tabs.cs` and `UI/Views/Shared/BreadcrumbList.cs`
  do not explain inactive discovery prerequisites and direct users to the wrong page.
  Show effective eligibility and point to Settings > Integrations > Instructions.

UI paths are relative to `mod/Source/SlopWorld/`.

## 1. Add regression tests before fixes

- Extend daemon input tests: manual breadcrumb requests must fail while Breadcrumbs
  is off, before any paste is queued; enabled delivery must still work.
- Extend pending-delivery tests: queue ordinary and instructions breadcrumbs, then
  disable Instructions, global discovery, or per-agent discovery before consuming.
  Only discovery must disappear. Turning Breadcrumbs off cancels everything;
  turning a switch back on must not resurrect cancelled or consumed text. Cover
  per-agent mount opt-out and retained preferences too.
- Add preview endpoint cases with zero, one, and multiple configured projects:
  explicit sample selection must render synthetic context, named selection must
  render that project, and unknown names must fail. Check unsaved template/path use.
- Protect unconditional worker bootstrap delivery with Instructions off, including
  custom prompts. Workers need their mailbox bootstrap regardless of manifest use;
  the planned fix is to unlock its editor, not stop task delivery.
- Where feasible, exercise production UI availability/reason logic through the
  game-free mod harness: worker prompt editing is independent of Instructions;
  discovery reports feature, mount, global discovery, and automatic-delivery
  prerequisites without erasing saved selections. Extract only a small helper if
  needed; avoid tests that merely search source text or recreate game widgets.

Run `make test-daemon` and `make test-mod`; record expected regression failures
before changing behavior. Existing correct-behavior cases should pass immediately.

## 2. Enforce delivery gates in the daemon

In `manager/capture_input.rs`, reject disabled manual breadcrumb delivery at the
manager boundary used by WebSocket requests. In `manager/start.rs` and
`manager/reconcile.rs`, retain enough pending-entry provenance to cancel discovery
without discarding ordinary breadcrumbs. Recheck current eligibility at consumption
so a configuration change cannot leave stale discovery in the next prompt. Preserve
one-shot delivery and existing mounts until restart; do not requeue on re-enable.

## 3. Make sample preview explicit

Align `InstructionsPage.cs` and `api/handlers_config.rs` on an explicit sample
selection. Preserve existing empty-project fallback for other callers if adding a
request field is necessary. Keep sample context independent of configured projects
and live sessions. Update the wire contract/generated output through make targets
if the request shape changes.

## 4. Align UI controls with effective behavior

Unlock the worker prompt editor in `WorkersPage.cs` and explain that bootstrap is
always submitted; keep worker discovery gated by Breadcrumbs. In
`EditSessionDialog.Tabs.cs` and `BreadcrumbList.cs`, distinguish stored discovery
preference from current delivery eligibility and show unmet prerequisites. Correct
the discovery-setting link to Settings > Integrations > Instructions, while feature
switch guidance continues to point to Settings > General > Experimental.

## 5. Validate and update behavior notes

Run `make test-daemon` and `make test-mod` again, plus `make lint-mod` for changed
game-bound C# code. Update `ui-settings.md` and relevant worker/protocol notes to
match the final behavior. Review all four feature-switch combinations and retained
preferences against the tests. Do not launch the game or inspect screenshots;
record visual validation as outstanding unless the user explicitly requests it.

Done when regression cases pass, manual and pending delivery honor their gates,
sample preview is correctly identified, and UI guidance matches runtime behavior.
