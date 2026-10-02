# Daemon usage polling

`usage/mod.rs` owns scheduling/aggregation; `rows.rs` owns catalog and row policy;
`providers.rs` owns credential reads and HTTP; `parsing.rs` interprets upstream JSON;
`anthropic_cache.rs` owns shared Anthropic request coordination. Daemon configuration
owns settings/defaults, and shared protocol owns wire fields and units.

Each provider has independent failure/backoff state and retains last-good values and
timestamps after failure. Cadence changes do not erase failure backoff. Claude's
credential-file modification can shorten its failure delay; Codex credentials are
not watched that way. Unknown upstream shapes are errors rather than invented values.

Rows resolve between network polls. An explicit provider row table disables that
provider when all configured rows are off. Enabled sources without data get
placeholders; after a valid partial table, omitted windows are inapplicable rather
than missing-data placeholders. One request can provide several windows; polling
uses the fastest effective enabled interval within provider cache/rate limits.

Client ages and discovered-row editing belong to [mod usage](mod-usage.md).
Setup belongs to [Usage polling](../docs/src/reference/integrations.md#usage-polling),
and shared credential behavior to [sandbox isolation](sandbox-isolation.md) and
[Configuring sandboxes](../docs/src/guides/configuring-sandboxes.md#credentials).
