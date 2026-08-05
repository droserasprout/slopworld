# House rules

- **Commit on `main`.** No branch, no PR. One person writes this repo and the
  history is a straight line; a branch is only something to merge back later.
- **`CLANKERS.md` is not edited or appended to.** What would have gone in it goes
  in a file here, and [index](index.md) gets a line pointing at it.
- Notes here are **short**, one subject a file. Delete what goes stale rather than
  keeping it hedged.
- A note distills a source comment; it does not replace one. The comment stays
  where the code is, because that is where it is read.
- `python3 tools/loc.py --comments --min=5` is what finds prose that has outgrown
  its file - see [build-commands](build-commands.md).
