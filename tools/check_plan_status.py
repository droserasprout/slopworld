#!/usr/bin/env python3
"""Check the status header in active plan notes."""

from pathlib import Path
import sys

STATUSES = {
    "proposed",
    "approved",
    "wip",
    "implemented",
    "reviewed",
    "human approved",
}


def check(path: Path) -> str | None:
    lines = path.read_text(encoding="utf-8").splitlines()
    if not lines or not lines[0].startswith("# "):
        return "expected a title on the first line"
    headers = [line for line in lines[1:] if line.startswith("Status:")]
    if len(headers) != 1:
        return "expected exactly one Status: line"
    if lines.index(headers[0]) > 4:
        return "Status: must be near the title"
    status = headers[0].removeprefix("Status:").strip()
    if status not in STATUSES:
        return f"invalid status {status!r}"
    return None


def main() -> int:
    paths = sorted(Path("notes").glob("plan-*.md"))
    errors = [(path, error) for path in paths if (error := check(path))]
    for path, error in errors:
        print(f"{path}: {error}", file=sys.stderr)
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
