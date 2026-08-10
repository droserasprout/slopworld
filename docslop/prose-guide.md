# Prose guide

- Comment rationale, invariants, external contracts, and counterintuitive framework behavior.
- Keep a comment beside the code whose safe modification depends on it.
- Put cross-file architecture and operational facts in `docslop/`.
- Delete narration of names, types, control flow, and expressions visible directly below it.
- Delete examples that restate code or tests.
- Delete implementation history unless it explains a constraint that still exists.
- Prefer one precise sentence; link to a devnote when the constraint needs more context.
- Remove stale prose instead of qualifying it until it can no longer be trusted.
