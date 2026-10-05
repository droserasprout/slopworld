#!/usr/bin/env python3
"""Count repository lines with and without comments.

Run `uv run --locked python -m tools.analysis.loc` to print a table by language.
Add paths to count only those paths.
Use `--docs` to include Markdown files.
The default excludes Markdown because documentation size does not measure source-code size.

Use `--comments` to print C# and Rust comments in file order.
The output removes comment markers and joins adjacent comment lines.
Add `--min=N` to show only blocks with at least N lines.
Use this option to find long comments that may belong in `notes/`.

The tool gets its file list from `git ls-files`.
This excludes untracked and ignored files without a separate exclusion list.

The tool counts an empty line as blank.
It counts a line as a comment only when the complete line is a comment.
A source line with a trailing comment counts as code, which matches cloc.
Python counts use its tokenizer and identify docstrings through its syntax tree.
Other languages use an approximate scanner; raw strings and nested block comments
are not recognized.
"""

import ast
import io
import os
import re
import subprocess
import sys
import tokenize
from pathlib import Path

# line comments, block comment pairs, string quotes.
C_LIKE = (('//',), (('/*', '*/'),), ('"', "'"))
HASH = (('#',), (), ('"', "'"))
XML = ((), (('<!--', '-->'),), ())

PY = HASH

LANGS = {
    '.cs': ('C#', C_LIKE),
    '.rs': ('Rust', C_LIKE),
    '.py': ('Python', PY),
    '.sh': ('Shell', HASH),
    '.just': ('Just', HASH),
    '.xml': ('XML', XML),
    '.csproj': ('XML', XML),
    '.toml': ('TOML', HASH),
    '.service': ('systemd', HASH),
    '.md': ('Markdown', ((), (), ())),
}

BY_NAME = {'justfile': ('Just', HASH)}

DOCS = {'Markdown'}


def syntax_for(path):
    named = BY_NAME.get(path.name)
    if named:
        return named
    return LANGS.get(path.suffix)


def scan(line, syn, block, *, rust=False):
    """Scan one line and return its code state, comment text, and block state.

    Join text from multiple comments on one line.
    Return None when the line has no comment.
    An empty string identifies a comment marker without text.
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
        # Detect blocks before strings because Python docstrings use quote characters.
        opened = next((b for b in blocks if line.startswith(b[0], i)), None)
        if opened:
            block = opened
            i += len(opened[0])
            # Record an empty value so a block opener still counts as a comment.
            said.append('')
            continue
        if ch in quotes:
            # Count a string as code. Treat an unclosed string as code through the end of the line.
            code = True
            if rust and ch == "'":
                lifetime = re.match(r"'[A-Za-z_][A-Za-z_0-9]*", line[i:])
                if lifetime and not line.startswith("'", i + len(lifetime[0])):
                    i += len(lifetime[0])
                    continue
            i += 1
            while i < len(line):
                if line[i] == chr(92):
                    i += 2
                elif line[i] == ch:
                    i += 1
                    break
                else:
                    i += 1
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
    """Return comment text without markers or indentation, or return None.

    Remove additional markers from documentation comments.
    Repository comments do not use a leading slash or asterisk as content.
    """
    if not said:
        return None
    return ' '.join(s.strip() for s in said).strip().lstrip('/*!').strip()


def count(path, syn):
    """Returns (total, blank, comment, code) for one file."""
    text = path.read_text(encoding='utf-8', errors='replace')
    if path.suffix == '.py':
        return count_python(text)
    total = blank = comment = code = 0
    block = None
    for line in text.splitlines():
        total += 1
        has_code, said, block = scan(line, syn, block, rust=path.suffix == '.rs')
        if has_code:
            code += 1
        elif said is not None:
            comment += 1
        elif not line.strip():
            blank += 1
        else:
            code += 1  # Count an unrecognized nonempty line as code.
    return total, blank, comment, code


def count_python(text):
    """Classify physical lines, counting only actual docstrings as comments."""
    tree = ast.parse(text)
    docstrings = set()
    for node in ast.walk(tree):
        if isinstance(node, (ast.Module, ast.ClassDef, ast.FunctionDef, ast.AsyncFunctionDef)):
            if node.body and isinstance(node.body[0], ast.Expr):
                value = node.body[0].value
                if isinstance(value, ast.Constant) and isinstance(value.value, str):
                    docstrings.add((value.lineno, value.col_offset))
    code_lines, comment_lines = set(), set()
    ignored = {tokenize.ENDMARKER, tokenize.ENCODING, tokenize.INDENT, tokenize.DEDENT, tokenize.NEWLINE, tokenize.NL}
    for token in tokenize.generate_tokens(io.StringIO(text).readline):
        if token.type in ignored:
            continue
        lines = range(token.start[0], token.end[0] + 1)
        target = (
            comment_lines
            if token.type == tokenize.COMMENT or (token.type == tokenize.STRING and token.start in docstrings)
            else code_lines
        )
        target.update(lines)
    total = len(text.splitlines())
    code = len(code_lines)
    comments = len(comment_lines - code_lines)
    return total, total - code - comments, comments, code


PROSE = {'C#', 'Rust'}


def blocks(path, syn):
    """Return comment blocks as a line number and a list of text lines.

    Join adjacent comment lines into one block.
    Keep a trailing source comment separate from the preceding block.
    """
    try:
        text = path.read_text(encoding='utf-8', errors='replace')
    except OSError as e:
        print(f'{path}: {e}', file=sys.stderr)
        return
    run, start, block = [], 0, None
    for n, line in enumerate(text.splitlines(), 1):
        has_code, said, block = scan(line, syn, block, rust=path.suffix == '.rs')
        if said is not None and not has_code:
            if not run:
                start = n
            run.append(said)
            continue
        if run:
            yield start, run
        run = []
        if said:  # Return a trailing comment as a separate one-line block.
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
        print(f'## {path} ({lang})')
        for n, run in kept:
            runs += 1
            print()
            head = f'L{n}'
            pad = ' ' * len(head)
            for i, said in enumerate(run):
                print(f'{head if i == 0 else pad}  {said}'.rstrip())
        print()
    print(f'# {runs} comments in {files} files.')


def tracked(paths):
    out = subprocess.run(
        ['git', 'ls-files', '-z', '--', *paths],
        capture_output=True,
        text=True,
        check=True,
    ).stdout
    return [Path(p) for p in out.split('\0') if p]


def measure(paths, docs=False, languages=None):
    """Returns per-language ``[files, lines, blank, comment, code]`` totals."""
    rows = {}
    for path in tracked(paths):
        found = syntax_for(path)
        if not found:
            continue
        lang, syn = found
        if (lang in DOCS and not docs) or (languages is not None and lang not in languages):
            continue
        if not path.is_file():  # a symlink to something outside the tree
            continue
        row = rows.setdefault(lang, [0, 0, 0, 0, 0])
        row[0] += 1
        for i, n in enumerate(count(path, syn)):
            row[i + 1] += n
    return rows


def main(argv):
    docs = '--docs' in argv
    least = next((int(a.split('=')[1]) for a in argv if a.startswith('--min=')), 1)
    paths = [a for a in argv if not a.startswith('-')]
    if '--comments' in argv:
        return show(paths, least)
    rows = measure(paths, docs=docs)

    head = ('language', 'files', 'lines', 'blank', 'comment', 'code')
    width = max([len(head[0])] + [len(language) for language in rows]) if rows else len(head[0])
    fmt = f'{{:<{width}}}  ' + '  '.join(['{:>7}'] * 5)
    print(fmt.format(*head))
    print('-' * (width + 2 + 9 * 5 - 2))
    total = [0, 0, 0, 0, 0]
    for lang, row in sorted(rows.items(), key=lambda kv: -kv[1][4]):
        print(fmt.format(lang, *row))
        total = [a + b for a, b in zip(total, row)]
    print('-' * (width + 2 + 9 * 5 - 2))
    print(fmt.format('total', *total))
    comment, code = total[3], total[4]
    print()
    print(f'{code + comment} lines of code with comments, {code} without.')


if __name__ == '__main__':
    try:
        main(sys.argv[1:])
    except BrokenPipeError:  # `| head`, and the reader has read enough.
        os.dup2(os.open(os.devnull, os.O_WRONLY), sys.stdout.fileno())
        sys.exit(0)
