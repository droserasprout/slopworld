# Daemon simplification: summary execution and task ownership

Status: implemented. These were independent follow-ups to the
[local refactors](daemon-refactor-small-plan.md). Both require more call-site review;
keep either change only if its final implementation removes code and indirection.

## Shared summary resolution — complete

`slopd/src/manager/task_summary.rs::run_task_summary` and
`slopd/src/manager/capture_title.rs::resolve_title_request` perform the same core work:
look up the prompt/instruction/model cache, otherwise clone request inputs and run
`crate::title::summarize` through `spawn_blocking`, then flatten the join result.

Extract that execution into a private manager helper. The implementation extracts the
narrower blocking request operation so callers retain session-specific cache/request
logging and return the result and whether it came from cache. Accept the prompt, summary
instruction, key-file path, and model explicitly. Keep configuration snapshots owned by
the callers, so this refactor does not change when settings are captured. A new request
type is justified only if it replaces existing duplication; avoid a provider trait or
summary-service framework.

Keep the following behavior in the callers:

- Task policy and minimum-length checks, including the policy recheck after completion.
- Session policy checks and conversation/generation checks that reject stale results.
- Applying a session title versus persisting a task summary, including missing-task handling.
- Cache writes after applicability checks; session titles also maintain `latest`.
- Existing diagnostic context, outcomes, and distinction between worker join failures
  and request failures.

The existing resolver logs cache hits and request starts with session-specific context.
Do not lose those logs just to shorten the function. If sharing the whole resolver needs
callbacks or logging policy parameters, extract only the blocking request operation.
That narrower change can still remove the repeated cloning and join-error handling.

Review existing title and task-summary coverage before editing. Validate cache hit/miss
behavior, disabled policy during an outstanding request, stale session generations,
and a task removed before completion. Use existing mocks where available; do not make
live provider requests. Preserve persistence and error behavior during the extraction.

## Move task forwarding to the task-store owner — complete

`slopd/src/manager/tasks.rs` defines a mutex-owning `TaskStore`, then implements most
task operations on `Manager` as one-line forwarding closures through that store.
This creates a manager API layer that contributes little policy.

Move the pure forwarding operations onto `TaskStore` as typed methods:
creation, worker-task creation, visible/all/get queries, updates, summary persistence,
cancellation, removal, and pruning. Update HTTP handlers and manager callers to use
those methods through the existing store field. Keep the concrete mutex private and
choose the narrowest visibility that reaches the existing callers.

Keep `Manager::fail_worker_task`: it handles empty IDs and lifecycle-specific logging.
Keep worker-session construction and summary scheduling on the manager. Do not move
authorization from HTTP handlers, change task visibility, or make every request go
through one generic task-operation enum.

Implementation sequence:

1. Search all manager task-method callers, including tests, worker startup, and summary
   completion. Distinguish durable task creation from worker-session construction.
2. Move signatures and mutex acquisition onto `TaskStore`; retain underlying `Tasks`
   operations and their current save/error behavior.
3. Update callers and remove obsolete forwarding methods and unused read/mutate helpers.
4. Check that no mutex guard escapes the store or survives across an await. Keep public
   results owned, as they are now.

This is an ownership change, not a task-store reliability redesign. Do not alter task
IDs, transitions, serialization, mutation-on-save-failure behavior, or worker cleanup.
If call-site verbosity cancels the reduction, retain the current owner boundary rather
than introducing forwarding macros to claim a smaller source file.

## Completion and validation

The two changes remain independently reviewable in the diff. Actual production-source
line impact is recorded from the implementation: shared summary execution added 40 and
removed 31 lines (net +9, while removing two duplicated blocking boundaries); task
ownership added 59 and removed 50 lines across the store and its callers (net +9, while
shrinking `manager/tasks.rs` by 3 lines and removing the Manager forwarding API).

Use `make format-daemon`, `make lint-daemon`, and `make test-daemon`. Retain coverage for
task authorization, visibility, terminal states, persistence, and worker failure. Add
focused regression tests only where the ownership move exposes a coverage gap.

No new features, dependencies, HTTP routes, configuration keys, or background workers
are needed. Do not launch the game or deploy the daemon. After implementation, update
`notes/daemon-files.md` and relevant ownership notes only where their descriptions
actually change, then mark the corresponding plan section complete.

Validation complete: `make format-daemon`, `make lint-daemon`, and `make test-daemon` pass.
