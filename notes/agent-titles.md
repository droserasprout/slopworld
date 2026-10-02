# Agent title ownership

The daemon owns managed-session summaries. [Session title policy](../slopd/src/session/title.rs)
owns composition, eligibility, and transitions; [title capture](../slopd/src/session/manager/capture/title.rs)
coordinates provider requests and commits. [The title provider and cache](../slopd/src/title/)
are separate from session policy. [SummariesPage](../mod/Source/SlopWorld/UI/Settings/SummariesPage.cs)
edits daemon policies and defaults.

Prompt capture excludes common approval answers, cancelled composers, history
recall, and uncertain edits. Host shell commands never enter this path. Current
managed-agent support covers Codex and Pi. Missing a title is preferable to sending
unrelated input.

Late results cannot replace a newer conversation or manually edited label. The
`once` policy consumes its attempt even on failure. Recovery may restore the current
conversation's cached title; a new conversation or run clears it. The cache stores
digests and summaries rather than submitted prompt text.

The provider reads its configured OpenRouter key in the daemon. This is not a
sandbox isolation guarantee: the Pi preset can separately forward
`OPENROUTER_API_KEY`. See [Summaries](../docs/src/guides/configuring-agents.md#summaries)
for settings and key handling, [Titles](../docs/src/tour/agents-and-projects.md#titles)
for user behavior, and [task mailboxes](agent-tasks.md) for delegated-task summaries.
