# Codebase simplification

Proposed: reduce repeated state, conversions and persistence mechanics while preserving
existing behavior. The strongest opportunities are in settings drafts and serialization;
large files alone are a poor guide because several daemon files contain substantial tests.
Findings come from static review of the working tree, including uncommitted changes, and
are candidates rather than measured savings or validated implementations.

Owners: [settings drafts](ui-settings.md), [configuration](daemon-config-stores.md),
[client](mod-client.md), and [wire contract](protocol-wire.md).

## Findings and order of work

1. **Unify draft-field state and numeric editing.** `DaemonConfigDraft` spreads field
   state across six dictionaries and a set. `SummariesPage` and `UsagePage` also retain
   raw text and normalization state, although `DaemonConfigState.Save` already applies
   queued normalizations. Introduce one field-state object per key, owning text, local
   and remote baselines, path, and normalization policy. Bind numeric controls to that
   state with explicit parsing/formatting rules. Remove duplicate page caches and lifecycle
   hooks as callers migrate. This is the highest-payoff candidate: fewer synchronized
   collections and fewer owners of the same edit, without a general-purpose form framework.
2. **Keep configuration patches structured until transport.** `DaemonConfig.ToPatchJson`
   builds nested JSON strings; `PatchObject` reparses them to compare values, and draft
   merging parses snapshots again. Construct an editable JSON object using the existing
   Json.NET infrastructure, compute a leaf diff, and serialize at the HTTP boundary.
   This has medium expected payoff and touches sensitive merge semantics. Keep the editable
   projection explicit rather than serializing every public field automatically.
3. **Share atomic file replacement mechanics.** Template, preset and configuration stores
   repeat temporary-file writes and renames; `paths::write_private_toml` already provides
   related mechanics. Consolidate replacement and failure cleanup while leaving validation,
   serialization and mutation locks with each store. Preserve permissions and asynchronous
   execution requirements. Expected size reduction is smaller than the settings work.
4. **Simplify preset response serialization.** `api/handlers_presets.rs` manually copies
   fields from serializable presets to add `source`. Consider a flattened response wrapper
   if automatically exposing future serialized preset fields is an accepted contract.
   Otherwise keep an explicit response projection. This is a small, independent cleanup.

## Acceptance constraints

- Drafts remain independent per page and endpoint. Reload merges untouched fields and
  reports same-field conflicts; Discard uses the latest remote snapshot. Save acknowledges
  only submitted values, preserving edits made while the request is in flight.
- Numeric fields retain invalid text until corrected. Usage's blank interval means
  inheritance, and clearing an existing override must still send the explicit zero needed
  by the daemon's deep merge. Opening a page must not create unintended overrides.
- Patches preserve omitted fields and unknown daemon configuration. Response metadata and
  secrets stay outside the editable projection. Missing factory metadata remains unavailable,
  not a locally invented default. Keep strict wire parsing and existing null semantics.
- Shared file replacement preserves each store's permission policy, lock boundaries and
  error behavior. Consolidating mechanics must not turn separate store updates into an
  implied transaction or introduce blocking filesystem work on async executor threads.
- Prefer changes that remove state or code overall. File splitting alone does not satisfy
  the goal; a new abstraction must retire repeated implementations at its call sites.

## Scope and validation

Preserve the established [daemon policy ownership boundaries](daemon-config-stores.md).
The [C# records proposal](plan-csharp-records.md) is separate: these changes do not require
new language features, and draft owners retain identity and coordinated mutation.

Terminal capture and the custom WebSocket transport need a separate investigation before
simplification. Their ordering, backpressure and compatibility requirements are documented
behavior, not demonstrated duplication. Generated bindings and test volume are likewise
not deletion targets merely because they contribute lines.

For implementation, use relevant Makefile build, lint and test targets. Reuse draft merge,
normalization and serialization tests; add focused regressions for changed semantics.
Verify persistence permissions and replacement failures if that helper changes, and preset
response equivalence if serialization changes. No game execution or image inspection is
needed for this plan. Static review did not run tests or establish a line-reduction estimate.

Complete each candidate with demonstrable removal of duplicate state or mechanics and
passing relevant checks. Record lasting ownership changes in the focused notes. Remove
resolved items, and delete this plan when all candidates are implemented or explicitly declined.
