# Title ownership refactor

Status: implemented

Title transitions were spread between input capture, labels, lifecycle, and result handling.
Summary persistence performed disk writes under the live-session lock.

The refactor centralizes title state and eligibility in `session/title.rs`, uses a private
request token to reject stale completions, and shares summary resolution between titles and
tasks. `title/cache.rs` persists ordered snapshots outside session and cache locks.

Review must preserve once-attempt consumption, uncertain-input exclusion, reset/rename
invalidation, cache recovery, and ordered clear after delayed writes. Approval answers are
excluded independently of the removed Waiting classifier.

Read `session/title.rs` and its tests, then `session/manager/capture/title.rs` and its tests,
then `title.rs` and `title/cache.rs` with cache tests. Ownership guidance lives in
[agent titles](agent-titles.md). Human review is pending.
