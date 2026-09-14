# Generated agent titles

The daemon owns managed-session summaries: it sees submitted input and OSC titles, while
keeping the OpenRouter credential outside agent sandboxes. Policies and defaults live in
the daemon config model; `UI/Settings/SummariesPage.cs` is the editor.

Prompt capture is a privacy boundary. Approval/dialog input, cancelled composers, history
recall and uncertain editing sequences must not become summarizer requests. Missing a title
is preferable to sending unrelated input. Host shell commands never enter this path.

A conversation epoch and request generation guard asynchronous results. `once` consumes its
attempt even on failure, preventing repeated billable attempts. Generated titles override OSC
redraws separately; durable-session restoration must not revive a previous conversation's title.
The cache stores digests and summaries, not submitted prompt text.

Managed Pi disables its project auto-name extension so two writers cannot compete. Claude
and OpenCode do not yet use this daemon path. The standalone Pi extension remains useful
outside SlopWorld.
