# Prose guide

These rules apply to source comments and `notes/` notes. Prose is a liability:
write it only when it preserves information that the code cannot show.

## Hard limits

- A source comment is one sentence and at most two physical lines. If it needs
  more, move the explanation to a devnote and leave a short link beside the code.
- A devnote has one subject and fits one page a person reads without scrolling
  far - roughly 700 words, with no cap on sections or bullets. Split larger notes
  by subject; the only exception is a protocol or state-machine sequence whose
  meaning would be damaged by splitting.
- Use one claim per sentence. Prefer one precise sentence over a paragraph of
  qualifications.
- Never add prose “for completeness.” If removing it does not hide a constraint,
  rationale, contract, or surprising behavior, remove it.

## Write only this

- Why a non-obvious choice is required.
- An invariant that a safe edit must preserve.
- An external, serialized, timing, or compatibility contract.
- Counterintuitive framework or runtime behavior.
- Cross-file architecture or operational facts that belong in `notes/`.

Put a fact beside the code whose safe modification depends on it. Put cross-file
facts in a short, focused devnote and link to it from the relevant code when useful.

A fact has one home. When it lives in a devnote, the code gets a link, not a
paraphrase. The same sentence must never appear in two files.

## Register

Length is not the only cost. These constructions read as insight, cost double, and
survive the review gate because each does carry a fact. Rewrite them flat:

- Antithesis and chiasmus: “a cap of zero is not a cap, it is a session that cannot
  start”, “a guardrail, not a boundary”. State what the code does.
- Trailing flourish: a clause that restates the sentence as an image, “rather than
  let the agent fail to fork on its first breath”.
- Rhetorical emphasis: bolding a claim, “all separation happens here”, “exactly”,
  “simply works”.
- Headings with a stance: “Terminal key routing”, not “The terminal, which is our
  problem exactly”.
- First-person plural and reader address: “what this costs us”, “our problem”.
- Suspense: the fact belongs at the front of the sentence, not the end.

Use the project’s own names for things instead of a metaphor for them.

## Delete this

- Narration of names, types, control flow, or expressions visible in the code below.
- Examples that merely restate code, tests, or an already-stated rule.
- Implementation history, author intent, or blame unless it explains a constraint
  that still exists.
- Generic advice, introductions, conclusions, summaries, and “note that” filler.
- Speculation and hedging. State the known constraint; do not preserve uncertainty
  as prose unless the uncertainty itself changes how the code must be used.

## Review gate

Before keeping a comment or note:

1. Delete it temporarily. Does the reader lose a fact needed to modify or use the
   code safely? If not, leave it deleted.
2. Is the fact visible directly in the code or test? Delete the prose.
3. Is it still true? Delete stale prose; do not keep weakening it with qualifiers.
4. Can it be reduced to one direct sentence or a small list of facts? Do that.
5. Does any clause sound like a line worth quoting? That clause is ornament. Cut it
   and check whether the fact survived; if it did, keep it cut.
6. Does this sentence already exist somewhere else in the repo? Keep one copy and
   link to it.
7. If it exceeds the limit, split it or move detail into a more appropriate,
   separately focused note.
