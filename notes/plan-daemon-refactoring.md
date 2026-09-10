# Daemon simplification plan

Status: implemented; steps 1, 2, 4, and 5 completed; steps 3 and 6 skipped after measurement.

Reduce production Rust LoC and branching without adding features or changing wire
formats, persisted formats, authorization, diagnostics, or lifecycle behavior.
Implement the steps as small, independently reviewable changes in the order below.
Keep existing regression tests; do not count deleting tests or comments as savings.

## 1. Consolidate summary-cache locking

Source: [title.rs](../slopd/src/title.rs), `SummaryCache`.

- Put `entries` and `latest` in one private `CacheState` behind one mutex.
- Rewrite insertion, remembering, clearing, and saving around that state; remove
  repeated paired locks and the helper needed only to coordinate them.
- Preserve poison recovery, serialization, cache-key construction, read-driven
  recency, eviction order, and whether each operation updates `latest`.
- Check round trips, cached task summaries leaving session titles intact, clearing,
  and eviction. Preserve the existing in-memory behavior when persistence fails.

## 2. Share breadcrumb-reference traversal

Source: [manager/library.rs](../slopd/src/manager/library.rs), library mutations.

- Add small immutable/mutable iterators over project and session breadcrumb lists
  in `Config`; use them for rename, detach, and attachment checks.
- Keep project-specific attachment changes explicit. Avoid a generic CRUD layer.
- Preserve validation order, duplicate prevention, list order, built-in restrictions,
  and the rule rejecting deletion of an attached breadcrumb.
- Check renaming references in both owners, moving a breadcrumb between projects,
  converting it to another library kind, and rejecting attached deletion.
- Preserve announcement behavior, including which mutations announce projects.

## 3. Share cache-file reading where it removes code

Sources: [activity.rs](../slopd/src/activity.rs), [title.rs](../slopd/src/title.rs).

- Extract only common file-read/TOML-decode mechanics into a small helper.
  Return enough error information for callers to retain their diagnostic messages
  and tracing targets; missing files remain a silent empty-cache case.
- Keep each cache's version checks, normalization, eviction, and file shape local.
- Check missing, unreadable, malformed, and unsupported-version files, plus valid
  entries requiring normalization. Use fixtures that do not depend on root-sensitive
  permission-denial behavior.
- Skip this extraction if preserving diagnostics requires more plumbing than the
  duplicated code it removes. Existing private-file writing is already shared.

## 4. Simplify configuration-change bookkeeping

Source: [manager/config.rs](../slopd/src/manager/config.rs).

- Replace `ConfigChange.old` with the token-change comparison computed after
  restoring a redacted token. Stop cloning the old config solely for publication.
- Combine the identical `JsonPatch` and `RawReplacement` effect categories;
  retain their distinct parsing and persistence paths.
- Combine project/library announcement flags, which currently always agree.
- Preserve the serialized persistence, publication, auth invalidation, and endpoint
  update sequence; release the persistence lock before reconciliation/announcements.
- Check redacted, empty, and changed tokens; rejected writes; disk reload; unknown
  fields retained by JSON patches; and each origin's reconciliation/events.
  Preserve no-op structured mutations and retry behavior after failed disk reloads.

## 5. Remove constant WebSocket handler results

Source: [api/ws.rs](../slopd/src/api/ws.rs), client-message dispatch.

- Make handlers that always return `true` return `()`; return continuation once
  from dispatch. Keep subscription-send failure as an explicit termination result.
- Remove `async` from the audio handler if it still performs no asynchronous work.
- Preserve per-message read/write checks, root-only actions, ignored unauthorized
  requests, logging, and the separate bounded scroll lane.
- Check a failed initial screen send ends intake while denied commands and normal
  action failures leave the connection open.

## 6. Optional: share OpenRouter key resolution

Sources: [title.rs](../slopd/src/title.rs),
[usage/providers.rs](../slopd/src/usage/providers.rs), `read_key`.

- Both readers choose environment or expanded file path and trim the result, but
  their empty-key and I/O errors differ. Share only the mechanics if the resulting
  helper and caller-specific error mapping yield a net reduction.
- Preserve fresh reads, environment fallback only for blank file settings, exact
  caller diagnostics, and never logging or persisting credentials.
- Check blank settings, whitespace-only keys, missing files, and expanded paths.
  Defer if the abstraction is larger than the two readers.

## Validation and completion

- Use `make` for project commands. For each implementation change, run
  `make format-daemon`, `make lint-daemon`, and `make test-daemon`.
- Add behavioral tests only for relevant gaps in existing coverage. Do not launch
  the game, take screenshots, or alter live daemon configuration for these checks.
- Record production LoC before/after each step separately from tests and notes.
  `make loc-report` provides a repository baseline; account for newly added helpers.
- Accept a step only when it reduces duplication or control flow and produces a
  net production-code reduction. Record actual savings rather than forecasts.
- Update focused architecture notes only when ownership or documented behavior
  needs clarification. Mark completed or skipped steps here with the reason.

Session cleanup, process capture, and provider polling already have shared paths.
Leave those abstractions intact. File splitting alone is outside this plan.

## Completion

- Summary-cache locking, breadcrumb traversal, configuration bookkeeping, and WebSocket dispatch
  were consolidated without changing their wire, persistence, authorization, diagnostic, or
  lifecycle contracts.
- The cache-file and OpenRouter-key extractions were skipped: preserving each caller's distinct
  diagnostics and error handling left the shared helpers larger than the duplicated production
  code they removed.
- `make format-daemon`, `make lint-daemon`, and `make test-daemon` pass. The daemon test target
  reports 19 wire tests, 412 Rust tests with one ignored, and 26 additional tests.
- Rust production code measured 29,492 lines at the baseline in
  [loc-20260910-213411.md](loc-20260910-213411.md) and 29,453 lines in the final
  snapshot [loc-daemon-refactoring-final-ours.md](loc-daemon-refactoring-final-ours.md), a
  net reduction of 39 code lines. The physical source delta is -37 lines across the four
  completed steps; the step-1 snapshot is preserved in
  [loc-daemon-refactoring-step-1.md](loc-daemon-refactoring-step-1.md).
