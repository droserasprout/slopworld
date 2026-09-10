# Daemon simplification: summary execution and task ownership

Status: implemented, including a follow-up to reduce the first implementation's
line count. See also [local refactors](daemon-refactor-small-plan.md).

## Summary execution

`title::summarize_async` owns input cloning, blocking execution, and join-error
conversion. It returns one `Result<String>` and accepts a worker label so session
and task diagnostics retain their original text. Provider errors pass through
unchanged. This operation needs no manager state and lives beside `summarize`.

The session and task callers retain their configuration snapshots, cache decisions,
policy checks, and result application. In particular:

- Session results still check conversation and generation before application.
- Tasks still recheck summary policy and tolerate removal before completion.
- Cache writes remain after applicability checks; task summaries do not change
  the latest session title.
- Existing cache-hit, request-start, and failure diagnostics remain in the callers.

## Task ownership

`manager/tasks.rs::TaskStore` exposes typed operations over its private mutex.
HTTP handlers and manager operations call the store directly. Local `Task` and
`Status` imports keep signatures short without forwarding macros or new abstractions.

`Manager::fail_worker_task` retains its empty-ID check and lifecycle logging.
Authorization remains in HTTP handlers, and worker-session construction and summary
scheduling remain on the manager. Store results are owned; no mutex guard escapes
or crosses an await. Task IDs, transitions, persistence, and save-failure behavior
are unchanged.

## Validation and scope

Run `make format-daemon`, `make lint-daemon`, and `make test-daemon` after changes.
Retain existing task authorization, persistence, worker lifecycle, and title-cache
coverage. No game execution or live provider requests are needed.

The initial ownership commit added 17 Rust source lines overall. The follow-up removes
23 lines. The combined ownership work is six lines smaller than its starting point.
These counts exclude notes; no tests or explanatory comments were removed to meet
the reduction. Formatting, daemon lint, and daemon tests pass (457 passed, one ignored).
