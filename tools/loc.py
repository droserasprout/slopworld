#!/usr/bin/env python3
"""Counts what this repo is made of, with and without its comments.

Run `python3 tools/loc.py` for a table by language, or hand it paths
(`python3 tools/loc.py slopd mod/Source`) to count only those. `--docs` adds the
markdown, which is otherwise left out: CLANKERS.md alone is longer than most of
the files it describes, so counting it with them says nothing about either.

`--comments` prints the comments instead of counting them - every comment in the
C# and Rust, in file order, markers stripped and neighbouring lines joined into
one block, which is the shape something reading the whole repo's prose at once
wants. Add `--min=N` to keep only blocks of N lines or more, which is the quick
way to find the paragraphs that have grown into documentation and belong in
`docslop/` instead.

Files come from `git ls-files`, so anything untracked or ignored - build output,
`mod/Assemblies`, `target/` - is out by construction rather than by a list of
directories to skip that would need keeping in step with .gitignore.

A line is blank if there is nothing on it, a comment if everything on it is one,
and code otherwise - a line of code with a comment after it is code, which is
cloc's rule and the one that makes "code" mean what a reader would guess. The
scanner knows string literals well enough that a `//` inside one is not a
comment; what it does not know is Rust's raw strings or anything else where the
quote is spelled differently, which in this repo is a handful of lines at most.
"""

import os
import subprocess
import sys
from pathlib import Path

# line comments, block comment pairs, string quotes.
C_LIKE = (("//",), (("/*", "*/"),), ('"', "'"))
HASH = (("#",), (), ('"', "'"))
XML = ((), (("<!--", "-->"),), ())

# Python's docstrings are comments wherever they are not assigned to anything,
# which is every one in this repo, so the triple quotes are spelled as a block.
PY = (("#",), (('"""', '"""'), ("'''", "'''")), ("'", '"'))

LANGS = {
    ".cs": ("C#", C_LIKE),
    ".rs": ("Rust", C_LIKE),
    ".py": ("Python", PY),
    ".sh": ("Shell", HASH),
    ".xml": ("XML", XML),
    ".csproj": ("XML", XML),
    ".toml": ("TOML", HASH),
    ".service": ("systemd", HASH),
    ".md": ("Markdown", ((), (), ())),
}

BY_NAME = {"Makefile": ("Make", HASH)}

DOCS = {"Markdown"}


def syntax_for(path):
    named = BY_NAME.get(path.name)
    if named:
        return named
    return LANGS.get(path.suffix)


def scan(line, syn, block):
    """Walks one line. Returns (has code, comment text, block still open).

    The text is what the comment markers on this line enclose, joined if there
    is more than one of them, and None when there are none. It is not falsiness
    that says whether the line had a comment, because a bare `//` has a comment
    and no text, and cloc and this counter both call that line a comment.
    """
    lines, blocks, quotes = syn
    code = False
    said = []
    i = 0
    while i < len(line):
        if block is not None:
            end = block[1]
            at = line.find(end, i)
            if at < 0:
                said.append(line[i:])
                return code, join(said), block
            said.append(line[i:at])
            i, block = at + len(end), None
            continue
        ch = line[i]
        # Blocks before quotes, because Python's block *is* a quote: `"""` read
        # a character at a time is a string that opens and closes immediately.
        opened = next((b for b in blocks if line.startswith(b[0], i)), None)
        if opened:
            block = opened
            i += len(opened[0])
            # Empty, so that a line whose block opens at the end of it is still
            # a line with a comment on it. The join drops it if it stays empty.
            said.append("")
            continue
        if ch in quotes:
            # A string is code, and the rest of it is nothing else. An unclosed
            # one is a line continuation we do not follow: take the rest as code.
            code = True
            end = line.find(ch, i + 1)
            i = len(line) if end < 0 else end + 1
            continue
        opened = next((c for c in lines if line.startswith(c, i)), None)
        if opened:
            said.append(line[i + len(opened) :])
            return code, join(said), None
        if not ch.isspace():
            code = True
        i += 1
    return code, join(said), block


def join(said):
    """The comment text of one line: markers gone, indentation gone, or None.

    A doc comment's extra marker (`///`, `//!`, `/** ... * ...`) is left over
    once the opener is cut, and reads as content unless it goes too. Stripping
    the whole leading run costs nothing real: no comment in either language
    starts on a slash or a star that the reader was meant to see.
    """
    if not said:
        return None
    return " ".join(s.strip() for s in said).strip().lstrip("/*!").strip()


def count(path, syn):
    """Returns (total, blank, comment, code) for one file."""
    try:
        text = path.read_text(encoding="utf-8", errors="replace")
    except OSError as e:
        print(f"{path}: {e}", file=sys.stderr)
        return 0, 0, 0, 0
    total = blank = comment = code = 0
    block = None
    for line in text.splitlines():
        total += 1
        has_code, said, block = scan(line, syn, block)
        if has_code:
            code += 1
        elif said is not None:
            comment += 1
        elif not line.strip():
            blank += 1
        else:
            code += 1  # inside a block we cannot read: call it code, not nothing.
    return total, blank, comment, code


PROSE = {"C#", "Rust"}


def blocks(path, syn):
    """The comments of one file, as (first line number, [text, ...]) runs.

    Neighbouring comment lines are one run: a paragraph broken across four
    `//` lines is one thought, and reading it as four is reading it wrong. A
    comment sitting after code on its own line ends the run, because the thing
    it is about is that line and not the sentence above it.
    """
    try:
        text = path.read_text(encoding="utf-8", errors="replace")
    except OSError as e:
        print(f"{path}: {e}", file=sys.stderr)
        return
    run, start, block = [], 0, None
    for n, line in enumerate(text.splitlines(), 1):
        has_code, said, block = scan(line, syn, block)
        if said is not None and not has_code:
            if not run:
                start = n
            run.append(said)
            continue
        if run:
            yield start, run
        run = []
        if said:  # a trailing comment: its own one-line run, then nothing.
            yield n, [said]
    if run:
        yield start, run


def show(paths, least):
    """Prints every comment in the C# and the Rust, file by file."""
    files = runs = 0
    for path in tracked(paths):
        found = syntax_for(path)
        if not found or found[0] not in PROSE or not path.is_file():
            continue
        lang, syn = found
        kept = [(n, r) for n, r in blocks(path, syn) if len(r) >= least]
        if not kept:
            continue
        files += 1
        print(f"## {path} ({lang})")
        for n, run in kept:
            runs += 1
            print()
            head = f"L{n}"
            pad = " " * len(head)
            for i, said in enumerate(run):
                print(f"{head if i == 0 else pad}  {said}".rstrip())
        print()
    print(f"# {runs} comments in {files} files.")


def tracked(paths):
    out = subprocess.run(
        ["git", "ls-files", "-z", "--", *paths],
        capture_output=True,
        text=True,
        check=True,
    ).stdout
    return [Path(p) for p in out.split("\0") if p]


def main(argv):
    docs = "--docs" in argv
    least = next((int(a.split("=")[1]) for a in argv if a.startswith("--min=")), 1)
    paths = [a for a in argv if not a.startswith("-")]
    if "--comments" in argv:
        return show(paths, least)
    rows = {}
    for path in tracked(paths):
        found = syntax_for(path)
        if not found:
            continue
        lang, syn = found
        if lang in DOCS and not docs:
            continue
        if not path.is_file():  # a symlink to something outside the tree
            continue
        row = rows.setdefault(lang, [0, 0, 0, 0, 0])
        row[0] += 1
        for i, n in enumerate(count(path, syn)):
            row[i + 1] += n

    head = ("language", "files", "lines", "blank", "comment", "code")
    width = max([len(head[0])] + [len(l) for l in rows]) if rows else len(head[0])
    fmt = f"{{:<{width}}}  " + "  ".join(["{:>7}"] * 5)
    print(fmt.format(*head))
    print("-" * (width + 2 + 9 * 5 - 2))
    total = [0, 0, 0, 0, 0]
    for lang, row in sorted(rows.items(), key=lambda kv: -kv[1][4]):
        print(fmt.format(lang, *row))
        total = [a + b for a, b in zip(total, row)]
    print("-" * (width + 2 + 9 * 5 - 2))
    print(fmt.format("total", *total))
    comment, code = total[3], total[4]
    print()
    print(f"{code + comment} lines of code with comments, {code} without.")


if __name__ == "__main__":
    try:
        main(sys.argv[1:])
    except BrokenPipeError:  # `| head`, and the reader has read enough.
        os.dup2(os.open(os.devnull, os.O_WRONLY), sys.stdout.fileno())
        sys.exit(0)
