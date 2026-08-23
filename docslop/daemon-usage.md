# Quota polling (`usage.rs`)

## Anthropic

Polls Claude Code's `/usage` endpoint with the OAuth token in
`~/.claude/.credentials.json`, re-read each poll and never copied or logged.
Anthropic rows are enabled by default; a row under `[daemon.usage_items.<key>]` can disable
individual windows. `SLOPD_USAGE_URL` overrides the endpoint.

- `parse` rejects unknown payloads as no windows plus an error; a failed poll keeps the
  last good windows. `expiresAt`, `refreshTokenExpiresAt` and `now_ms()` use epoch
  milliseconds. An expired access token does not mean logout because Claude Code refreshes it
  lazily; only an expired refresh token requires `claude auth`.
- The credentials file is `shared` ([sandbox-isolation](sandbox-isolation.md)), so an
  agent refresh updates the host file. Its mtime is checked every loop; a change clears
  failure backoff and makes the poll due. Stale files keep backing off.
- A bind mount cannot be atomically renamed (`EBUSY`), so in-sandbox Claude uses an
  `O_TRUNC` write on the host inode. A poll can see a partial file; `read_creds` retries
  parse errors once after 50ms, but not IO errors.
- Failures back off exponentially to 30 minutes; `Retry-After` wins. 429 is parsed
  without becoming an HTTP error, and the wait is included in the mod-facing error.
- Payload units: `utilization` is a percentage here (unlike Messages API fractions),
  `extra_usage`/`spend` utilization is money, `monthly_limit` is cents, and
  `resets_at` is RFC3339 with a numeric offset. Windows match `five_hour` and
  `seven_day*`; money is marked `unit: usd`, and reset values cross the wire as seconds.

## OpenRouter

An enabled `[daemon.usage_items.openrouter_balance]` row polls `/api/v1/credits` (override
with `SLOPD_CREDITS_URL`) and exposes `openrouter_balance`: credits bought are `limit`, spent
credits are `amount`. OpenRouter is off until that row is enabled because there is no host
login from which to infer a key.

- `openrouter_key_file` reads fresh each poll; blank uses `OPENROUTER_API_KEY` from
  slopd's environment. Neither path logs, copies or writes the key.
- `parse_credits` accepts both `total_credits`/`total_usage` and older `limit`/`usage`,
  and requires both figures. A null limit means no credit limit, not a full bar; no
  credit bought reports 100% spent.

## OpenAI / Codex

Enabled OpenAI rows read the current Codex token and optional account ID from `~/.codex/auth.json`
(`openai_credentials` overrides) and poll the primary/secondary windows. They never use the
refresh token or send file contents. The endpoint is
undocumented, so `SLOPD_OPENAI_USAGE_URL` supports fixtures and unknown payloads draw no
figures. Rows are `openai_session` and `openai_week`; 401/403 says `codex login`.

## All together

- Each source has its own `Poller`, due time and failure count. A 429 or bad login does
  not stall other sources; disabling one clears only its rows.
- The Settings > Integrations > Usage table stores per-window entries under
  `[daemon.usage_items.<key>]`. Each entry has `poll = true/false` and an optional
  `interval_secs`; an omitted or zero interval inherits `daemon.usage_poll_secs`.
  Anthropic and OpenAI rows default to enabled; OpenRouter rows default to disabled. Once rows
  for a source are present, the source is enabled when any of its rows is enabled.
- A provider request can answer several windows at once. The daemon schedules that request at
  the fastest enabled row interval, while each window keeps its own due time and disabled rows
  are removed from the merged snapshot immediately.
- `merge` exposes one wire `windows` list. `ok` requires every enabled source to be
  current; errors are joined. With no source enabled, the snapshot draws nothing.
- `sources` records enabled sellers even before they answer, allowing the mod to hold
  placeholder rows for an expired login.
- The loop checks `config.toml` at least every 30s (`LOOK`), so GUI switches take effect
  without a restart.

Drawn by [mod-ui-chrome](mod-ui-chrome.md)'s `UsageReadout`.
