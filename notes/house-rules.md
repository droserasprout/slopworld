# House rules

- **Commit on `main`.** This repository has one author and a linear history.
  Use branches only for work that will be merged back; pull requests are outside
  the workflow.
- **`AGENTS.md` is not edited or appended to.** What would have gone in it goes
  in a file here, and [index](index.md) gets a line pointing at it.
- Notes here are **short**, one subject a file. Delete what goes stale rather than
  keeping it hedged.
- A note distills a source comment; it does not replace one. The comment stays
  where the code is, because that is where it is read.
- **`docs/` outranks these notes.** Those pages are written and approved by a
  human; these are not. When a note disagrees with a page, fix the note - see
  [human-docs](human-docs.md).
- `python3 tools/loc.py --comments --min=5` is what finds prose that has outgrown
  its file - see [build-commands](build-commands.md).
