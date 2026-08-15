# Refactor: usage.rs poll loop

Owns: `slopd/src/usage.rs`. Nothing else. See [daemon-usage](daemon-usage.md).

`spawn` is the highest cognitive complexity in the repo (146 lines, cognitive
111). The loop body is three near-identical provider blocks — Anthropic,
OpenRouter, OpenAI — each shaped: check enabled → `clear`, else if due →
`spawn_blocking(read → fetch → parse)` → `settle`. Only the three fn triples
differ.

Steps:

- Describe a provider as data: `{ enabled: fn(&Daemon) -> bool, poller: &mut
  Poller, read, fetch, parse }` (fn pointers, or a small trait). Keep the
  settle/backoff in one `poll_one(&mut Poller, now, base)` helper.
- Drive `[anth, cred, openai]` through the helper in one loop.
- The `merge` + `sources` tail and the `due`/`wait` computation then iterate the
  same slice instead of naming each provider three times.

Watch: the creds-file-stamp early-renewal (the `stamp != creds_stamp` block) is
**Anthropic-only** — see its comment. Keep it an optional per-provider hook; do
not force it on OpenRouter/OpenAI.

Done when: one loop body covers all three providers, and adding a fourth seller is
a table row rather than a new block.
