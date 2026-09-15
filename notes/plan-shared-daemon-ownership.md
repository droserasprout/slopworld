# Daemon ownership of shared policy

Worker implementation: `7a4daefa`. The daemon now publishes config defaults,
usage catalog/rows, project-path previews and terminal capabilities. Review found
the following unresolved acceptance constraints; keep this plan until they pass.

## Remaining work

- Remove handwritten daemon defaults/prompts from the mod. Missing metadata must
  produce an explicit unavailable reset/preview state, not another compiled copy
  of daemon policy. Only connection bootstrap and independent client safety limits
  belong locally.
- Coalesce temporary-project preview requests while the name is unchanged. A draw
  must not advance the request generation and invalidate an outstanding reply.
  Handle errors with bounded retry and visible status; retain stale-name rejection.
- Merge static config usage metadata with dynamically discovered catalog entries.
  Merely opening settings must not synthesize `poll = false` for a live dynamic
  window or save that value as a new override.
- Resolve row eligibility against provider enablement as well as per-window
  overrides. A disabled provider must not retain apparently polled placeholders
  for its implicit default windows.
- Restore instruction-breadcrumb preview behavior using daemon rendering. The
  sandbox summary currently drops it, and the instructions editor ignores the new
  response field. Preserve an explicitly empty draft breadcrumb instead of
  substituting the saved value.
- Restore independent client allocation bounds and validate advertised terminal
  limits. Runtime capacity is daemon-owned, but must not remove client memory
  safeguards or allow invalid min/max values into layout and history arithmetic.

## Validation

Add regressions covering repeated draws before a preview reply, dynamic usage
windows when config metadata is present, provider disablement through a partial
usage table, missing factory metadata, empty breadcrumb drafts and malformed or
oversized terminal limits. Use relevant Makefile checks without running the game
or inspecting images. Preserve unrelated worktree edits.

The worker reported daemon/mod lint and tests passing (268 mod tests); review also
ran `make test-wire-contract` (9 passed). Those checks do not cover the cases above.
Update focused ownership notes to describe the final behavior and remove this plan
only after implementation and review satisfy these constraints.
