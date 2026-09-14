# Usage polling

Start in `usage/` for provider adapters and `usage.rs` for scheduling/aggregation. Provider
response fixtures and config definitions own field names, units and defaults.

Each provider has independent failure/backoff state. Failed polls retain its last successful
values; disabling it clears its rows. An enabled source with no data still needs a placeholder.
One provider request may supply multiple windows: poll at the fastest enabled interval, while
respecting the provider-wide cache/rate limit. Missing optional windows must not bypass it.

Anthropic's rotating credential is a shared bind. Atomic rename over the mount fails, so
an in-place writer can briefly expose partial JSON; credential reads retry parse failure.
Credential changes reset backoff. An expired access token alone is not proof of logout.

The Anthropic cache and lock prevent restart/duplicate-process request bursts. Never store
credentials in that cache. A zero `Retry-After` must not defeat the minimum 429 backoff.

Codex usage is an undocumented endpoint: unknown payloads must show no fabricated figures.
Window identity follows duration, not primary/secondary position. OpenRouter credit absence
is not equivalent to a full balance. Use adapter fixtures, never account polling, for tests.
