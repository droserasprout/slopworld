# Quota polling (`usage.rs`)

## Anthropic

Asks the endpoint Claude Code's own `/usage` does, with the OAuth token in
`~/.claude/.credentials.json`, **re-read per poll and never copied or logged**.
`[daemon] usage = false` stops it reading the file; `SLOPD_USAGE_URL` points it
elsewhere.

- `parse` **recognises rather than assumes**: an unrecognised payload leaves *no*
  windows and an error, so being wrong reads as "no numbers" and never as a colony
  at zero. A failed poll keeps the last good windows and adds the reason.
- The credentials file's **mtime is watched every lap** and a change puts the
  Anthropic poller back to due with its failure count cleared. A login renewed on
  the host is the only thing that turns "expired" back into numbers, and waiting
  out the backoff that a dead token earned means half an hour of a wrong tooltip.
  A stale token whose file has not moved keeps backing off, there being nothing to
  learn by asking again.
- `backoff` doubles per consecutive failure, capped at half an hour;
  `Retry-After` beats both. A 429 is read rather than raised
  (`http_status_as_error(false)`), and the wait goes into the error string because
  the mod draws it.
- Payload traps:
  - `utilization` is a *percentage* here, a fraction in the Messages API headers.
  - `extra_usage`/`spend` carry a `utilization` too, but theirs is **money**, so
    the family match keeps a quota row from becoming a dollar row.
  - `monthly_limit` is **cents**.
  - `resets_at` is RFC3339 with a numeric offset, applied rather than assumed.
- Windows are matched by family (`five_hour`, `seven_day*`), so a plan with
  different limits needs no change on either side. Money rides over stamped
  `unit: usd`. Resets go over as **seconds remaining**, so the countdown survives
  the daemon.

## OpenRouter

Second seller, same file. `[daemon] openrouter = true` polls `/api/v1/credits`
(`SLOPD_CREDITS_URL` overrides) and lands one `balance` window: credits bought as
the `limit`, credits spent as the `amount`, so the mod's existing "what is left"
subtraction is the balance. Off by default - unlike Claude's, there is no login on
the host to infer a key from.

- The key is `openrouter_key_file`, and **blank means slopd's own environment**
  (`OPENROUTER_API_KEY`), which is where the `pi` preset forwards it from. Either
  road reads fresh per poll; neither logs, copies or writes it back.
- `parse_credits` recognises both shapes (`total_credits`/`total_usage`, and the
  older `limit`/`usage`) and wants *both* figures - a balance is a subtraction. A
  null limit is "no credit limit on this key" rather than a full bar. Nothing
  bought is `pct: 100`, or an empty account draws like an untouched one.

## Both together

- **Two `Poller`s, not one loop asking both**: each keeps its own `due` and failure
  count, so a 429 on one side never slows the other and a bad key never takes the
  other's numbers off the screen. Switching one off `clear`s its rows only.
- `merge` is what the wire sees: one `windows` list, because the mod draws
  resources rather than sellers. `ok` is *every* live source being current - a
  stale row nobody could tell from a live one is what this must never draw - and
  the errors are joined, so the tooltip says which half is out. Neither source
  enabled is the default snapshot, which draws nothing.
- `sources` is which sellers are **switched on**, answering or not, added after the
  merge (which knows snapshots, not switches). It is what lets the mod hold a row's
  place for a seller that has never answered: without it a login that expired
  before the daemon started is a strip with nothing on it.
- The loop looks at `config.toml` at least every 30s (`LOOK`), which is how a
  switch thrown in the GUI takes hold without a restart.

Drawn by [mod-ui-chrome](mod-ui-chrome.md)'s `UsageReadout`.
