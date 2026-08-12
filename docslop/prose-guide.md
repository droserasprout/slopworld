# Prose guide

These rules apply to source comments and `docslop/` notes. Prose is a liability:
write it only when it preserves information that the code cannot show.

## Hard limits

- A source comment is one sentence and at most two physical lines. If it needs
  more, move the explanation to a devnote and leave a short link beside the code.
- A devnote has one subject and is at most 120 words or eight bullets. Split
  larger notes by subject; the only exception is a protocol or state-machine
  sequence whose meaning would be damaged by splitting.
- Use one claim per sentence. Prefer one precise sentence over a paragraph of
  qualifications.
- Never add prose “for completeness.” If removing it does not hide a constraint,
  rationale, contract, or surprising behavior, remove it.

## Write only this

- Why a non-obvious choice is required.
- An invariant that a safe edit must preserve.
- An external, serialized, timing, or compatibility contract.
- Counterintuitive framework or runtime behavior.
- Cross-file architecture or operational facts that belong in `docslop/`.

Put a fact beside the code whose safe modification depends on it. Put cross-file
facts in a short, focused devnote and link to it from the relevant code when useful.

## Delete this

- Narration of names, types, control flow, or expressions visible in the code below.
- Examples that merely restate code, tests, or an already-stated rule.
- Implementation history, author intent, or blame unless it explains a constraint
  that still exists.
- Generic advice, introductions, conclusions, summaries, and “note that” filler.
- Speculation and hedging. State the known constraint; do not preserve uncertainty
  as prose unless the uncertainty itself changes how the code must be used.
- Duplicate explanations in a comment, devnote, README, and test.

## Review gate

Before keeping a comment or note:

1. Delete it temporarily. Does the reader lose a fact needed to modify or use the
   code safely? If not, leave it deleted.
2. Is the fact visible directly in the code or test? Delete the prose.
3. Is it still true? Delete stale prose; do not keep weakening it with qualifiers.
4. Can it be reduced to one direct sentence or a small list of facts? Do that.
5. If it exceeds the limit, split it or move detail into a more appropriate,
   separately focused note.
