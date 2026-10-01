# Usage polling

Start in `usage/`: `mod.rs` owns scheduling/aggregation, `rows.rs` owns the catalog and
row policy, `providers.rs` owns credentials/HTTP, and `anthropic_cache.rs` owns shared
Anthropic request coordination. Response fixtures and config definitions own field names,
units and defaults.

Each provider has independent failure/backoff state. Failed polls retain the provider's last successful values.
Merged snapshots carry each provider's last-good timestamp. The client anchors row ages
and reset countdowns to that poll, independently of the aggregate failure timestamp.
Repeated snapshots of one poll preserve the client's monotonic anchor.
Disabling the provider clears its rows. An enabled source with no data still needs a placeholder,
but a partial usage table also disables implicit default rows for that provider. The mod merges
the static config catalog with dynamically discovered windows and keeps unedited dynamic rows
out of the saved override table. Once a provider returns a valid partial table, omitted catalog windows are not applicable.
The daemon excludes them from resolved rows.
It retains placeholders only while that provider supplies no usable window.
One provider request may supply multiple windows.
Poll at the fastest effective enabled interval, including implicit catalog rows, within the
provider-wide cache and rate limits. Missing optional windows must not bypass it. Cadence
changes do not erase failure backoff. Resolve row toggles between polls, including placeholders.

Anthropic's rotating credential is a shared bind. Atomic rename over the mount fails.
An in-place writer can briefly expose partial JSON.
Credential reads retry after parse failure.
The host and sandbox share Codex's `~/.codex/auth.json` bind mount. Do not copy its refresh-token
lineage into each sandbox. Credential changes reset backoff. An expired access token alone is not
proof of logout.

The Anthropic cache and lock prevent restart/duplicate-process request bursts. Cache identity
includes the credential path, endpoint, and an access-token SHA-256 digest; token rotation
conservatively partitions cached data and backoff without storing tokens. Lock acquisition
creates the cache parent and waits at most one second. Contention defers the request; other
filesystem failures permit an uncached request. Hold the guard through the post-lock cache
check and save. Both cached data and retry state require the current format version.
A zero `Retry-After` must not defeat the minimum 429 backoff.

The Codex usage endpoint is undocumented.
Do not invent figures for unknown payloads.
Window identity follows duration, not primary/secondary position. OpenRouter credit absence
is not equivalent to a full balance. Use adapter fixtures for tests. Never poll accounts for tests.
