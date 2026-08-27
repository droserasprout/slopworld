# House rules

- **Commit on `main`.** This repository has one author and a linear history.
  Use branches only for work that will be merged back; pull requests are outside
  the workflow.
- **Keep `AGENTS.md` concise.** Keep topical facts in a file here, and update its
  curated links and [index](index.md) when notes are added, moved, or renamed.
- Notes here are **short**, one subject a file. Delete what goes stale rather than
  keeping it hedged.
- A note distills a source comment; it does not replace one. The comment stays
  where the code is, because that is where it is read.
- **`docs/` outranks these notes.** Those pages are written and approved by a
  human; these are not. When a note disagrees with a page, fix the note - see
  [human-docs](human-docs.md).
- `python3 tools/loc.py --comments --min=5` is what finds prose that has outgrown
  its file - see [build-commands](build-commands.md).
