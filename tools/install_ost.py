#!/usr/bin/env python3
"""Install the latest dated OST tracks and update the mod's OST catalog."""

from __future__ import annotations

import argparse
import re
import shutil
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_SOURCE_DIR = ROOT / ".ost-staging"
TRACK_NAMES = ("pace", "dive", "hime", "dawn")
TRACK_RE = re.compile(r"^(?P<name>pace|dive|hime|dawn)-(?P<date>20\d{6})\.ogg$")
DATED_TRACK_RE = re.compile(r"^(?:pace|dive|hime|dawn)-20\d{6}\.(?:flac|ogg)$")
RADIO = ROOT / "mod/Source/SlopWorld/Sim/Jukebox/Radio.cs"
SONGS = ROOT / "mod/Defs/Songs.xml"
DEST_PARENT = ROOT / "mod/Sounds/SlopWorld"
DEST_DIR = DEST_PARENT / "OST"


def latest_tracks(source_dir: Path, wanted_date: str | None) -> tuple[str, dict[str, Path]]:
    grouped: dict[str, dict[str, Path]] = {}
    for path in source_dir.glob("*.ogg"):
        match = TRACK_RE.fullmatch(path.name)
        if not match or not path.is_file():
            continue
        grouped.setdefault(match.group("date"), {})[match.group("name")] = path

    complete = {
        stamp: tracks
        for stamp, tracks in grouped.items()
        if all(name in tracks for name in TRACK_NAMES)
    }
    if wanted_date:
        if wanted_date not in complete:
            raise SystemExit(f"no complete dated OST set for {wanted_date} in {source_dir}")
        stamp = wanted_date
    elif complete:
        stamp = max(complete)
    else:
        raise SystemExit(f"no complete pace/dive/hime/dawn OGG set in {source_dir}")
    return stamp, {name: complete[stamp][name] for name in TRACK_NAMES}


def verify_radio() -> None:
    text = RADIO.read_text()
    if '"Sounds", "SlopWorld", "OST"' not in text:
        raise SystemExit("Radio.cs does not point at the OST directory")


def update_songs(stamp: str) -> None:
    entries = []
    for name in TRACK_NAMES:
        entries.append(
            "  <SongDef>\n"
            f"    <defName>SlopWorld_{name}_{stamp}</defName>\n"
            f"    <clipPath>SlopWorld/OST/{name}-{stamp}</clipPath>\n"
            "    <volume>1</volume>\n"
            "    <tense>false</tense>\n"
            "  </SongDef>"
        )
    SONGS.write_text('<?xml version="1.0" encoding="utf-8"?>\n<Defs>\n'
                     + "\n".join(entries) + "\n</Defs>\n")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--source-dir",
        type=Path,
        default=DEFAULT_SOURCE_DIR,
        help=f"directory containing dated split OGGs (default: {DEFAULT_SOURCE_DIR})",
    )
    parser.add_argument("--date", help="install a specific YYYYMMDD set")
    parser.add_argument("--dry-run", action="store_true", help="show changes without writing")
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    verify_radio()
    stamp, tracks = latest_tracks(args.source_dir, args.date)
    print(f"installing OST date {stamp}")
    for name, source in tracks.items():
        destination = DEST_DIR / source.name
        print(f"  {source} -> {destination}")
    wanted_names = {source.name for source in tracks.values()}
    obsolete = []
    for directory in (DEST_PARENT, DEST_DIR):
        if not directory.is_dir():
            continue
        obsolete.extend(
            path
            for path in directory.iterdir()
            if path.is_file()
            and path.name not in wanted_names
            and DATED_TRACK_RE.fullmatch(path.name)
            and path not in obsolete
        )
    for path in obsolete:
        print(f"  remove obsolete {path}")

    if args.dry_run:
        print(f"  verify {RADIO}")
        print(f"  update {SONGS}")
        return

    DEST_DIR.mkdir(parents=True, exist_ok=True)
    for source in tracks.values():
        shutil.copy2(source, DEST_DIR / source.name)
    for path in obsolete:
        path.unlink()
    update_songs(stamp)
    print(f"updated {SONGS}")


if __name__ == "__main__":
    main()
