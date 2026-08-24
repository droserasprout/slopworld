#!/usr/bin/env python3
"""Find common LLM cliches in prose and source comments."""

import argparse
import bisect
import fnmatch
import json
import re
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True)
class Match:
    start: int
    end: int
    count: int | None = None


@dataclass(frozen=True)
class Rule:
    id: str
    name: str
    pattern: re.Pattern[str] | None = None
    chain_head: str | None = None
    chain_test: re.Pattern[str] | None = None

    def find(self, text: str):
        if self.pattern is not None:
            return [Match(match.start(), match.end()) for match in self.pattern.finditer(text)]
        return find_chains(text, self.chain_head, self.chain_test)


FLAGS = re.IGNORECASE
CHAIN_BODY = r"[^,.;:!?\n–—…]*"
CHAIN_SEP = r"(?:\s*,\s*(?:and\s+|or\s+)?|\s+(?:and|or)\s+|\s*[;&–—]\s*(?:and\s+|or\s+)?|\s+-{1,2}\s+)"
CHAIN_SPLIT = re.compile(CHAIN_SEP, FLAGS)


def regex_rule(rule_id, name, pattern):
    return Rule(rule_id, name, re.compile(pattern, FLAGS))


def chain_rule(rule_id, name, head, head_test):
    return Rule(rule_id, name, chain_head=head, chain_test=re.compile(head_test, FLAGS))


def find_chains(text, head, head_test):
    item = head + CHAIN_BODY
    chain = re.compile(rf"\b{item}(?:{CHAIN_SEP}{item})+", FLAGS)
    found = []
    for match in chain.finditer(text):
        end = match.end()
        while end > match.start() and text[end - 1].isspace():
            end -= 1
        count = sum(bool(head_test.match(part.strip())) for part in CHAIN_SPLIT.split(match.group()))
        found.append(Match(match.start(), end, count))
    return found


RULES = (
    chain_rule("no-chain", "No X, no Y chains", r"no[-\s]", r"^no[-\s]"),
    regex_rule("whole", "That's the whole ...", r"\b(?:that|this)(?:['’]s|\s+(?:is|was))\s+the\s+whole\b(?:\s+\w+)?"),
    chain_rule(
        "did-not-chain",
        "Did not X, did not Y chains",
        r"(?:did\s+not|didn['’]t)\s",
        r"^(?:did\s+not|didn['’]t)\s",
    ),
    regex_rule(
        "dont-verb-it",
        "Don't VERB it ... VERB it",
        r"\b(?:do\s+not|don['’]t)\s+(?:just\s+|simply\s+|merely\s+)?(\w+)(?:\s+(?:of|about|at|on|for|with|to))?\s+it\b[^.!?\n]*?[.!?;,:–—]['\"”’]*\s*(?:just\s+|simply\s+|merely\s+)?\1(?:\s+(?:of|about|at|on|for|with|to))?\s+it\b",
    ),
    regex_rule(
        "sit-with",
        "Sit with that",
        r"\bsit(?:s|ting)?\s+with\s+(?:that|this|it|(?:the|your)\s+(?:discomfort|feelings?|tension|weight|uncertainty|ambiguity|grief|silence|unease))\b(?:\s+for\s+a\s+\w+)?",
    ),
    regex_rule(
        "already-know",
        "You already know",
        r"\byou\s+already\s+knows?\s+(?:the\s+answer|what|how|why|this|that|it|who|where)\b|\byou\s+already\s+knows?\b(?![ \t]+\w)",
    ),
    regex_rule("is-the-entire", "Is the entire ...", r"(?:\b(?:is|was|are|were)|['’]s)\s+the\s+entire\b(?:\s+\w+)?"),
    regex_rule(
        "the-entire-is",
        "The entire ... is",
        r"\bthe\s+entire\s+[\w'’-]+(?:\s+[\w'’-]+){0,4}?\s+(?:is|was|are|were)\b",
    ),
    regex_rule(
        "is-real",
        "Is real ... and/not",
        r"\bis\s+(?:(?:the|a)\s+real\b(?![\s-]+(?:estate|time|life|world|quick)\b)[^.!?\n]*?\b(?:and|not)\s+it\b|real\b(?![\s-]+(?:estate|time|life|world|quick)\b)[^.!?\n]*?\b(?:and|not)\b)",
    ),
    regex_rule("punchline", "The punchline is", r"\bthe\s+punchline(?:\s+(?:is|was|being)\b|\s*[:?])"),
    regex_rule(
        "worth-naming",
        "Worth naming",
        r"(?:\b(?:is|are|was|were|feels?|felt|seems?|seemed)|['’]s)\s+(?:\w+\s+){0,2}?worth\s+naming\b(?!\s+names\b)|\bworth\s+naming\s*:",
    ),
    regex_rule(
        "not-nothing",
        "That's not nothing",
        r"\b(?:that|this|it|which)(?:['’]s|\s+(?:is|was))\s+not\s+nothing\b",
    ),
    regex_rule(
        "ai-vocab",
        "AI vocabulary words",
        r"\b(?:delv(?:e|es|ed|ing)|tapestr(?:y|ies)|meticulous(?:ly)?|pivotal|intricate(?:ly)?|intricacies|interplay|underscor(?:e|es|ed|ing)|garner(?:s|ed|ing)?|bolster(?:s|ed|ing)?|vibrant|bustling|multifaceted|seamless(?:ly)?|commendable|ever-evolving)\b",
    ),
    regex_rule(
        "not-just",
        "Not just X, but Y",
        r"\bnot\s+(?:just|only|merely|simply)\s+[^.!?\n;]*?\bbut(?:\s+also)?\b|\b(?:it|this|that)(?:['’]s|\s+(?:is|was))\s+not\s+[^.!?\n,;—–]{1,60}[,;—–]\s*(?:it|this|that)(?:['’]s|\s+(?:is|was))\b",
    ),
    regex_rule(
        "note-that",
        "It's important to note",
        r"\bit(?:['’]s|\s+(?:is|was))\s+(?:also\s+)?(?:important|worth|crucial|essential|vital)\s+(?:to\s+(?:note|remember|understand|recognize|mention)|noting|mentioning|remembering)\b(?:\s+that\b)?|\bit\s+should\s+be\s+noted\b",
    ),
    regex_rule(
        "testament",
        "Stands as a testament",
        r"\b(?:stand|stands|stood|serve|serves|served|standing|serving)\s+as\s+(?:a|an)\s+(?:\w+\s+)?(?:testament|reminder)\b|\b(?:is|was|are|were|remain|remains)\s+a\s+(?:\w+\s+)?testament\s+to\b",
    ),
    regex_rule(
        "crucial-role",
        "Plays a crucial role",
        r"\bplay(?:s|ed|ing)?\s+(?:a|an)\s+(?:\w+\s+)?(?:crucial|pivotal|vital|key|significant|central|critical|important)\s+role\b",
    ),
    regex_rule(
        "landscape",
        "Ever-evolving landscape",
        r"\b(?:ever-)?(?:evolving|changing|shifting)\s+landscape\b|\bin\s+today['’]s\s+(?:fast-paced|ever-changing|ever-evolving|digital|modern|competitive)\s+\w+",
    ),
    regex_rule(
        "vague-experts",
        "Experts argue",
        r"\b(?:many|some|several|most|numerous)?\s*(?:experts|critics|observers|scholars|analysts|commentators)\s+(?:have\s+|often\s+|widely\s+)?(?:argu(?:e|es|ed)|not(?:e|es|ed)|suggest(?:s|ed)?|believ(?:e|es|ed)|agree[ds]?|contend(?:s|ed)?|observ(?:e|es|ed)|caution(?:s|ed)?|claim(?:s|ed)?|cit(?:e|es|ed)|point(?:s|ed)?\s+out)\b|\bindustry\s+reports?\s+(?:suggest|indicate|show)\w*\b",
    ),
    regex_rule(
        "despite-challenges",
        "Despite these challenges",
        r"\bdespite\s+(?:these|those|such|its|their|the|numerous|significant|ongoing)\s+(?:\w+\s+)?challenges\b|\bfac(?:e|es|ed|ing)\s+(?:several|numerous|many|significant|various|a\s+number\s+of)\s+challenges\b|\bchallenges\s+remain\b|\bremains\s+to\s+be\s+seen\b",
    ),
    regex_rule(
        "participle-tail",
        "Participle sentence tail",
        r",\s+(?:highlighting|underscoring|emphasizing|showcasing|reflecting|demonstrating|illustrating|signaling|solidifying|cementing|reinforcing|underlining)\s+(?:its|his|her|their|our|the|a|an|how|that|what|both)\b[^.!?\n]*",
    ),
    regex_rule(
        "promo",
        "Promotional boilerplate",
        r"\bnestled\s+(?:in|on|among|between|along|at)\b|\bin\s+the\s+heart\s+of\b|\brich\s+(?:cultural\s+|historical\s+)?(?:heritage|history|tapestry)\b|\bhidden\s+gem\b|\bmust-(?:visit|see|try)\b|\bbreathtaking\b|\bboasts?\s+(?:a|an|the)\b|\bstunning\s+(?:views?|scenery|architecture|backdrop)\b",
    ),
    regex_rule(
        "ai-leftovers",
        "Chatbot leftovers",
        r"\bas\s+an\s+ai(?:\s+language)?\s+model\b|\bas\s+of\s+my\s+last\s+(?:update|training)\b|\bknowledge\s+cutoff\b|\bI\s+(?:cannot|can['’]t|do\s+not|don['’]t)\s+(?:browse\s+the\s+internet|access\s+real-?time)\b|contentReference|oaicite|turn0(?:search|news|image)\d*|attributableIndex|utm_source=",
    ),
)

RULES_BY_ID = {rule.id: rule for rule in RULES}
PROSE_SUFFIXES = {".md", ".mdx", ".rst", ".txt"}
C_LIKE_SUFFIXES = {".c", ".cc", ".cpp", ".cs", ".go", ".h", ".hpp", ".java", ".js", ".jsx", ".rs", ".ts", ".tsx"}
HASH_SUFFIXES = {".ini", ".py", ".service", ".sh", ".toml", ".yaml", ".yml"}
XML_SUFFIXES = {".csproj", ".html", ".svg", ".xml"}
NAMED_HASH_FILES = {"Dockerfile", "Makefile", "meson.build"}


def blank(text):
    return ["\n" if char == "\n" else " " for char in text]


def mask_markdown(text):
    out = list(text)
    fenced = False
    fence = ""
    offset = 0
    for line in text.splitlines(keepends=True):
        marker = re.match(r"^[ \t]{0,3}(`{3,}|~{3,})", line)
        if marker and (not fenced or marker.group(1)[0] == fence):
            fenced = not fenced
            fence = marker.group(1)[0] if fenced else ""
            out[offset : offset + len(line)] = blank(line)
        elif fenced:
            out[offset : offset + len(line)] = blank(line)
        else:
            for code in re.finditer(r"(`+)(.+?)\1", line):
                out[offset + code.start() : offset + code.end()] = blank(code.group())
        offset += len(line)
    return "".join(out)


def mask_commit_message(text):
    out = list(mask_markdown(text))
    offset = 0
    scissors = False
    for line in text.splitlines(keepends=True):
        stripped = line.lstrip()
        if scissors or stripped.startswith("#"):
            out[offset : offset + len(line)] = blank(line)
        if stripped.startswith("#") and "------------------------ >8 ------------------------" in stripped:
            scissors = True
        offset += len(line)
    return "".join(out)


def skip_quoted(text, start):
    quote = text[start]
    triple = text.startswith(quote * 3, start)
    token = quote * (3 if triple else 1)
    i = start + len(token)
    while i < len(text):
        if text.startswith(token, i):
            return i + len(token)
        if not triple and text[i] == "\\":
            i += 2
        else:
            i += 1
    return len(text)


def mask_c_like(text):
    out = blank(text)
    i = 0
    while i < len(text):
        if text.startswith("//", i):
            end = text.find("\n", i)
            end = len(text) if end < 0 else end
            out[i + 2 : end] = text[i + 2 : end]
            i = end
        elif text.startswith("/*", i):
            end = text.find("*/", i + 2)
            end = len(text) if end < 0 else end
            out[i + 2 : end] = text[i + 2 : end]
            i = min(len(text), end + 2)
        elif text[i] in "'\"":
            i = skip_quoted(text, i)
        else:
            raw = re.match(r"r(#+)?\"", text[i:])
            if raw:
                terminator = '"' + (raw.group(1) or "")
                end = text.find(terminator, i + raw.end())
                i = len(text) if end < 0 else end + len(terminator)
            else:
                i += 1
    return "".join(out)


def mask_hash(text, python=False):
    out = blank(text)
    i = 0
    while i < len(text):
        if python and (text.startswith("'''", i) or text.startswith('\"\"\"', i)):
            token = text[i : i + 3]
            end = text.find(token, i + 3)
            end = len(text) if end < 0 else end + 3
            out[i:end] = text[i:end]
            i = end
        elif text[i] == "#":
            end = text.find("\n", i)
            end = len(text) if end < 0 else end
            out[i + 1 : end] = text[i + 1 : end]
            i = end
        elif text[i] in "'\"":
            i = skip_quoted(text, i)
        else:
            i += 1
    return "".join(out)


def mask_xml(text):
    out = blank(text)
    for match in re.finditer(r"<!--(.*?)(?:-->|$)", text, re.DOTALL):
        start = match.start() + 4
        end = match.end() - (3 if match.group().endswith("-->") else 0)
        out[start:end] = text[start:end]
    return "".join(out)


def text_kind(path):
    if path.suffix.lower() in PROSE_SUFFIXES:
        return "prose"
    if path.suffix.lower() in C_LIKE_SUFFIXES:
        return "c-like"
    if path.suffix.lower() in HASH_SUFFIXES or path.name in NAMED_HASH_FILES:
        return "python" if path.suffix.lower() == ".py" else "hash"
    if path.suffix.lower() in XML_SUFFIXES:
        return "xml"
    return None


def lintable_text(path, text):
    kind = text_kind(path)
    if kind == "prose":
        return mask_markdown(text) if path.suffix.lower() in {".md", ".mdx"} else text
    if kind == "c-like":
        return mask_c_like(text)
    if kind in {"hash", "python"}:
        return mask_hash(text, python=kind == "python")
    if kind == "xml":
        return mask_xml(text)
    return ""


def collect_matches(text, enabled):
    raw = []
    for rule in RULES:
        if rule.id in enabled:
            raw.extend((match.start, -match.end, rule, match) for match in rule.find(text))
    raw.sort(key=lambda item: (item[0], item[1]))
    kept = []
    for _, _, rule, match in raw:
        if kept and match.start < kept[-1][1].end:
            continue
        kept.append((rule, match))
    return kept


def locations(text):
    starts = [0]
    starts.extend(match.end() for match in re.finditer("\n", text))
    return starts


def line_column(starts, offset):
    line_index = bisect.bisect_right(starts, offset) - 1
    return line_index + 1, offset - starts[line_index] + 1


def excerpt(text, start, end):
    line_start = text.rfind("\n", 0, start) + 1
    line_end = text.find("\n", end)
    line_end = len(text) if line_end < 0 else line_end
    value = re.sub(r"\s+", " ", text[line_start:line_end]).strip()
    return value if len(value) <= 100 else value[:97] + "..."


def repository_files(paths):
    if paths:
        found = []
        for raw in paths:
            path = Path(raw)
            if path.is_dir():
                result = subprocess.run(
                    ["git", "ls-files", "--cached", "--others", "--exclude-standard", "-z", "--", raw],
                    capture_output=True,
                    text=True,
                )
                if result.returncode == 0:
                    found.extend(Path(item) for item in result.stdout.split("\0") if item)
                else:
                    found.extend(candidate for candidate in path.rglob("*") if candidate.is_file())
            else:
                found.append(path)
        return found
    result = subprocess.run(
        ["git", "ls-files", "--cached", "--others", "--exclude-standard", "-z"],
        check=True,
        capture_output=True,
        text=True,
    )
    return [Path(item) for item in result.stdout.split("\0") if item]


def parse_args(argv):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("paths", nargs="*", help="files or directories; read prose from stdin with -")
    parser.add_argument("--commit-msg", metavar="FILE", help="lint a Git commit-message file; use - for stdin")
    parser.add_argument("--exclude", action="append", default=[], metavar="GLOB")
    parser.add_argument("--rule", action="append", choices=sorted(RULES_BY_ID), dest="rules")
    parser.add_argument("--format", choices=("text", "json"), default="text")
    parser.add_argument("--list-rules", action="store_true")
    args = parser.parse_args(argv)
    if args.commit_msg and args.paths:
        parser.error("paths cannot be combined with --commit-msg")
    return args


def main(argv=None):
    args = parse_args(argv)
    if args.list_rules:
        for rule in RULES:
            print(f"{rule.id}\t{rule.name}")
        return 0

    enabled = set(args.rules or RULES_BY_ID)
    paths = [Path(args.commit_msg)] if args.commit_msg else repository_files(args.paths)
    failures = 0
    for path in sorted(paths, key=lambda item: str(item)):
        shown = str(path)
        if shown == "-":
            original = sys.stdin.read()
            linted = mask_commit_message(original) if args.commit_msg else original
            shown = "<stdin>"
        else:
            if (not args.commit_msg and text_kind(path) is None) or any(fnmatch.fnmatch(shown, glob) for glob in args.exclude):
                continue
            if not path.is_file():
                continue
            try:
                original = path.read_text(encoding="utf-8")
            except (OSError, UnicodeError) as error:
                print(f"{shown}: {error}", file=sys.stderr)
                return 2
            linted = mask_commit_message(original) if args.commit_msg else lintable_text(path, original)
        starts = locations(original)
        for rule, match in collect_matches(linted, enabled):
            failures += 1
            line, column = line_column(starts, match.start)
            detail = excerpt(original, match.start, match.end)
            if match.count is not None:
                detail += f" ({match.count} items)"
            if args.format == "json":
                print(json.dumps({"path": shown, "line": line, "column": column, "rule": rule.id, "message": rule.name, "excerpt": detail}, ensure_ascii=False))
            else:
                print(f"{shown}:{line}:{column}: cliche/{rule.id}: {rule.name}: {detail}")
    if failures and args.format == "text":
        print(f"{failures} cliche{'s' if failures != 1 else ''} found", file=sys.stderr)
    return 1 if failures else 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except BrokenPipeError:
        sys.exit(0)
