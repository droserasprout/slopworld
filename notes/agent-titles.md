# Agent titles

[Session policy](../slopd/src/session/title.rs) owns summary eligibility and transitions;
[capture](../slopd/src/session/capture/title.rs) coordinates requests and commits.
The [provider/cache](../slopd/src/title/) is shared with task summaries but does not
own either feature's policy.

Prompt capture skips approval answers, cancelled or uncertain edits, and history
recall. Host commands never enter this path. Missing a title is preferable to sending
unrelated input. Late results cannot replace a newer conversation or manual label.
The `once` policy consumes its attempt even on failure. Recovery may restore the
current conversation's cached title; a new conversation or run clears it. The cache
stores digests and summaries, not prompt text.

The daemon reads the provider key. This does not isolate the key from agents: the Pi
preset can also forward `OPENROUTER_API_KEY`. Settings and key handling belong to
[Summaries](../docs/src/agents/usage-and-summaries.md#prompt-summaries).
