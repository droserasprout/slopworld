#!/usr/bin/env python3
"""Counts what this repo is made of, with and without its comments.

Run `python3 tools/loc.py` for a table by language, or hand it paths
(`python3 tools/loc.py slopd mod/Source`) to count only those. `--docs` adds the
markdown, which is otherwise left out: CLANKERS.md alone is longer than most of
the files it describes, so counting it with them says nothing about either.

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
    """Walks one line. Returns (has code, has comment, block still open)."""
    lines, blocks, quotes = syn
    code = comment = False
    i = 0
    while i < len(line):
        if block is not None:
            end = block[1]
            at = line.find(end, i)
            comment = True
            if at < 0:
                return code, comment, block
            i, block = at + len(end), None
            continue
        ch = line[i]
        # Blocks before quotes, because Python's block *is* a quote: `"""` read
        # a character at a time is a string that opens and closes immediately.
        opened = next((b for b in blocks if line.startswith(b[0], i)), None)
        if opened:
            block = opened
            i += len(opened[0])
            comment = True
            continue
        if ch in quotes:
            # A string is code, and the rest of it is nothing else. An unclosed
            # one is a line continuation we do not follow: take the rest as code.
            code = True
            end = line.find(ch, i + 1)
            i = len(line) if end < 0 else end + 1
            continue
        if any(line.startswith(c, i) for c in lines):
            return code, True, None
        if not ch.isspace():
            code = True
        i += 1
    return code, comment, block


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
        has_code, has_comment, block = scan(line, syn, block)
        if has_code:
            code += 1
        elif has_comment:
            comment += 1
        elif not line.strip():
            blank += 1
        else:
            code += 1  # inside a block we cannot read: call it code, not nothing.
    return total, blank, comment, code


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
    paths = [a for a in argv if not a.startswith("-")]
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
    main(sys.argv[1:])
