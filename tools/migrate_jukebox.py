#!/usr/bin/env python3
"""Migrate the pre-recognition jukebox likes file to the structured TOML shape."""

import argparse
import json
import os
from pathlib import Path
import sys


def default_input() -> Path:
    local = Path("jukebox.toml")
    if local.exists():
        return local
    data_home = os.environ.get("XDG_DATA_HOME")
    if not data_home:
        data_home = str(Path.home() / ".local" / "share")
    return Path(data_home) / "slopworld" / "jukebox.toml"


def string(value) -> str:
    return "" if value is None else str(value)


def legacy_records(lines):
    records = []
    for line in lines:
        value = line.strip()
        if not value or value == "-" or value.startswith("#"):
            continue
        if "\t" in line:
            at, title = line.split("\t", 1)
            if at.strip() and title.strip():
                records.append({"at": at.strip(), "title": title.strip()})
                continue
        records.append({"title": value})
    return records


def old_records(text: str):
    lines = text.splitlines()
    table = next((i for i, line in enumerate(lines) if line.strip() == "[[like]]"), None)
    if table is None:
        # The first likes implementation was one plain title per line, with a later
        # timestamp<TAB>title form and `-` separator lines. Keep all of it migratable.
        return legacy_records(lines)

    records = legacy_records(lines[:table])
    try:
        import tomllib

        data = tomllib.loads("\n".join(lines[table:]))
        likes = data.get("like", [])
        if isinstance(likes, list):
            records.extend(likes)
    except (ImportError, ValueError):
        # Leave malformed TOML visible as legacy text rather than dropping the file's
        # trailing history altogether.
        records.extend(legacy_records(lines[table:]))
    return records


def migrated(records) -> str:
    lines = [
        "# Migrated by tools/migrate_jukebox.py; new likes append here.",
        "",
    ]
    for record in records:
        if not isinstance(record, dict):
            continue
        old_title = string(record.get("title"))
        lines.extend(
            [
                "[[like]]",
                f"at = {json.dumps(string(record.get('at')), ensure_ascii=False)}",
                f"source = {json.dumps(string(record.get('source')), ensure_ascii=False)}",
                f"artist = {json.dumps(string(record.get('artist')), ensure_ascii=False)}",
                f"title = {json.dumps(old_title, ensure_ascii=False)}",
                f"original_artist = {json.dumps(string(record.get('original_artist')), ensure_ascii=False)}",
                f"original_title = {json.dumps(string(record.get('original_title', old_title)), ensure_ascii=False)}",
                "",
            ]
        )
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("input", nargs="?", type=Path, default=default_input())
    parser.add_argument("-o", "--output", type=Path, default=Path("jukebox.new.toml"))
    parser.add_argument("--force", action="store_true")
    args = parser.parse_args()

    if not args.input.is_file():
        parser.error(f"old likes file does not exist: {args.input}")
    if args.output.exists() and not args.force:
        parser.error(f"output already exists: {args.output} (use --force to replace it)")

    args.output.write_text(migrated(old_records(args.input.read_text())), encoding="utf-8")
    print(args.output)
    return 0


if __name__ == "__main__":
    sys.exit(main())
