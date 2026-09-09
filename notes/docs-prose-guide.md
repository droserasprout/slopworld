# Prose guide

Comments and devnotes preserve constraints, rationale, contracts, and cross-file
facts that code and tests cannot show. Delete narration, stale history, generic
advice, and examples that only restate a rule.

- Keep source comments to one sentence and at most two physical lines. Move longer
  explanations to a devnote and link beside the code.
- Keep each devnote to one subject and roughly 700 words. Split by subject unless
  splitting would obscure a protocol or state-machine sequence.
- Give each fact one home; link to it elsewhere. Keep code-specific facts beside
  the code whose safe modification depends on them.
- State one claim per sentence, with the fact first. Use project names and direct
  headings; remove metaphors, rhetorical contrasts, emphasis, and restatements.

Before keeping prose, check that it is current, needed for safe use or modification,
and unavailable directly from code, tests, or another document. Then cut every
clause that adds no information.
