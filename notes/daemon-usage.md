# Usage polling

`usage/` owns provider reads, parsing, row policy, and scheduling with caller-supplied
monotonic time. `application/usage.rs` owns the background loop, retained snapshot,
and change-only publication. It reads accepted settings without retaining the session
manager. Wire fields and units belong to the shared protocol.

Providers fail and back off independently, retaining last-good values and timestamps.
Changing cadence must not erase backoff. Missing credentials hide retained readouts;
malformed credentials and request failures leave stale values visible. Unknown
upstream shapes are errors. Credential watching can shorten Claude's failure delay;
Codex does not use that mechanism.

The settings catalog retains discovered rows even when polling is disabled or
credentials are missing. Only explicitly enabled rows cause polling. One request may
serve several rows; use their fastest effective interval within provider limits.
Resolve rows between polls: enabled sources without data get placeholders, but windows
omitted from a valid partial response are inapplicable. Keep upstream availability
separate from display filtering, and retain absolute reset deadlines when rebasing
countdowns to a new poll timestamp.

[Mod usage](mod-usage.md) owns client ages and row editing. Setup belongs to
[Usage polling](../docs/src/agents/usage-and-summaries.md#usage-polling).
