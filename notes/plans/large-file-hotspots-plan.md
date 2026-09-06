# Large-file hotspots plan

Baseline: `tokei 14.0.0` on 2026-09-06, excluding build output and assets. Nine
files exceed 1,000 physical lines, but only two exceed 1,000 actual code lines.

The first extraction pass is complete. The two outliers now have focused seams:

- `usage.rs` keeps snapshot models, provider scheduling, filtering, backoff, and merging;
  response-shape parsing and its tests live in `usage/parsing.rs`, while provider I/O and
  caching remain in `usage/providers.rs`.
- `sandbox/bind.rs` remains the façade and security boundary; host-path resolution and the
  final `refused` checks live in `sandbox/bind/policy.rs`, while ordered bwrap argv emission
  lives in `sandbox/bind/mounts.rs`.

The refreshed focused inventory is `tokei 14.0.0`: `usage.rs` is 962 lines / 740 code and
`sandbox/bind.rs` is 787 / 700 code. The extracted files are 620 / 494, 520 / 419, 143 / 108,
and 383 / 291 respectively, so no extraction module is itself a new hotspot.

## Worst offenders at baseline

- [`slopd/src/usage.rs`](../../slopd/src/usage.rs) - 1,561 lines / 1,213 code.
  It combines usage models, provider polling, response parsing, scheduling,
  caching and retry behavior. If it keeps growing, split provider adapters from
  shared scheduling and merge logic first.
- [`slopd/src/sandbox/bind.rs`](../../slopd/src/sandbox/bind.rs) - 1,278 / 1,076.
  It combines bind resolution, path policy and mount construction. Split only
  along security-tested seams; do not make the bind guard less central.

The remaining large files are not immediate refactor targets: `seccomp.json` is
policy data, while `audio/mod.rs`, `sandbox/mod.rs`, `config.rs`,
`manager/config.rs`, `emu.rs` and `manager/library.rs` are cohesive façades or
subsystems at roughly 840-920 code lines each.

## Plan

1. Do not refactor by line count alone; wait for a behavior change or a clear
   ownership seam.
2. When either code outlier changes substantially, extract one stable concept at
   a time and keep its tests beside the owning module.
3. Re-run `make test` and `make lint` after each extraction, then refresh this
   inventory with `tokei`.

The extraction was checked with `make format-daemon`; the full project checks remain the handoff
step because this worktree also carries an unrelated emulator test failure in the pre-existing
capture-history changes.
