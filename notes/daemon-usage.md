# Usage polling

Start in `usage/` for provider adapters and `usage.rs` for scheduling/aggregation. Provider
response fixtures and config definitions own field names, units and defaults.

Each provider has independent failure/backoff state. Failed polls retain the provider's last successful values.
Disabling the provider clears its rows. An enabled source with no data still needs a placeholder,
but a partial usage table also disables implicit default rows for that provider. The mod merges
the static config catalog with dynamically discovered windows and keeps unedited dynamic rows
out of the saved override table. Once a provider returns a valid partial table, omitted catalog windows are not applicable.
The daemon excludes them from resolved rows.
It retains placeholders only while that provider supplies no usable window.
One provider request may supply multiple windows.
Poll at the fastest enabled interval within the provider-wide cache and rate limits. Missing optional windows must not bypass it.

Anthropic's rotating credential is a shared bind. Atomic rename over the mount fails.
An in-place writer can briefly expose partial JSON.
Credential reads retry after parse failure.
The host and sandbox share Codex's `~/.codex/auth.json` bind mount. Do not copy its refresh-token
lineage into each sandbox. Credential changes reset backoff. An expired access token alone is not
proof of logout.

The Anthropic cache and lock prevent restart/duplicate-process request bursts. Never store
credentials in that cache. A zero `Retry-After` must not defeat the minimum 429 backoff.

The Codex usage endpoint is undocumented.
Do not invent figures for unknown payloads.
Window identity follows duration, not primary/secondary position. OpenRouter credit absence
is not equivalent to a full balance. Use adapter fixtures for tests. Never poll accounts for tests.
