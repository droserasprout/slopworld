# Generated agent titles

The daemon owns managed-session summaries: it sees submitted input and OSC titles, while
keeping the OpenRouter credential outside agent sandboxes. Policies and defaults live in
the daemon configuration model.
`UI/Settings/SummariesPage.cs` is the editor.

Prompt capture is a privacy boundary. Approval/dialog input, cancelled composers, history
recall and uncertain editing sequences must not become summarizer requests. Missing a title
is preferable to sending unrelated input. Host shell commands never enter this path.

The per-session title owner keeps a private request token. Completion must match that exact
token; new conversations, label edits, rename, disable, reset, and replacement invalidate old work.
`once` consumes its attempt even on failure, preventing repeated billable attempts.
Generated titles override OSC redraws separately.
Restoring a persistent session must not restore a previous conversation's title.
`session/title.rs` owns composition, eligibility, and title transitions. Capture coordinates
provider calls and commits; task summaries reuse the provider/cache path without session policy.
Approval answers are excluded independently of terminal activity state.

`title/cache.rs` stores digests and summaries, not submitted prompt text. Mutations update
bounded memory in live-commit order. One background writer persists coalesced snapshots,
without holding the cache or session lock during filesystem I/O. Drop drains pending writes;
an abrupt exit can lose the latest recovery title. Disk failures retain in-memory state and
retry on the next mutation. Rename and clear share the same persistence order.

Managed Pi disables its project auto-name extension so two writers cannot compete. Claude
and OpenCode do not yet use this daemon path. The standalone Pi extension remains useful
outside SlopWorld.
