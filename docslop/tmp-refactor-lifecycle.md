# Refactor: manager/lifecycle.rs

Owns: `slopd/src/manager/lifecycle.rs`. Nothing else.

Five of the repo's top-15 most complex methods live in this one file:
`sync_from_config` (113 lines, cognitive 31), `run_errand` (102), `start` (92),
`file_action` (90), `new` (fan_in/out 28 each). It carries the whole manager
lifecycle.

Do first — **`sync_from_config`**. It runs four separable passes in one body:

1. retain-and-detach removed sessions (+ scroll/title/grant cleanup)
2. upsert config sessions into `live` (inline ~25-line `Live { … }` literal)
3. autostart pass
4. adopt orphans from tmux — re-locks `self.live` **four times per iteration**

Steps:

- Extract each pass to a private method: `prune_removed(&cfg) -> Vec<String>`,
  `upsert_sessions(&cfg)`, `autostart(&cfg)`, `adopt_orphans()`.
  `sync_from_config` becomes a ~6-line orchestrator.
- Move the `Live { … }` literal into `Live::new(cfg, title)` (or `Default` +
  overrides) so the field list is written once — it will bite when `Live` grows.
- In the adopt loop, take **one** `self.live` guard per name instead of
  read/write ×4; snapshot the size/emu check, then apply.
- If time allows, give `run_errand`/`start`/`file_action` the same pass-extraction
  treatment; they are long but not tangled like `sync_from_config`.

Done when: `sync_from_config` fits on a screen and `tokensave complexity` no
longer lists it. Pure split — behaviour unchanged.
