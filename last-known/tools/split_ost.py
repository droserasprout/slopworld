#!/usr/bin/env python3
"""Split the latest Bitwig OST export into dated 192 kbps OGG Vorbis tracks.

The arrangement points are intentionally fixed.  They were read from the
2026-08-12 export at 110 BPM and include the deliberate gap between dive and
hime while excluding the export's trailing silence.
"""

from __future__ import annotations

import argparse
import re
import subprocess
from dataclasses import dataclass
from datetime import date
from pathlib import Path


DEFAULT_SOURCE_DIR = Path("/home/droserasprout/Bitwig Studio/Projects/slopworld")
DEFAULT_OUTPUT_DIR = Path(__file__).resolve().parents[1] / ".ost-staging"
DATE_RE = re.compile(r"(?P<year>20\d{2})[-_](?P<month>\d{2})[-_](?P<day>\d{2})")
TRACK_RE = re.compile(r"^(?:pace|dive|hime|dawn)-20\d{6}\.(?:flac|ogg)$")


@dataclass(frozen=True)
class Track:
    name: str
    start: float
    end: float


# 110 BPM, hard-coded from the current arrangement.  The gap from 257.623243
# to 261.814263 is silence between exports, not part of either track.
TRACKS = (
    Track("pace", 0.000000, 130.909091),
    Track("dive", 130.909091, 257.623243),
    Track("hime", 261.814263, 432.000000),
    Track("dawn", 432.000000, 630.281950),
)


def export_date(path: Path) -> str:
    match = DATE_RE.search(path.name)
    if not match:
        raise SystemExit(f"cannot find YYYY-MM-DD in export name: {path.name}")
    try:
        value = date(
            int(match.group("year")),
            int(match.group("month")),
            int(match.group("day")),
        )
    except ValueError as exc:
        raise SystemExit(f"invalid date in export name: {path.name}") from exc
    return value.strftime("%Y%m%d")


def latest_export(source_dir: Path) -> Path:
    candidates = [
        path
        for path in source_dir.glob("*.flac")
        if path.is_file() and not TRACK_RE.fullmatch(path.name)
    ]
    if not candidates:
        raise SystemExit(f"no exported FLAC files found in {source_dir}")
    return max(candidates, key=lambda path: (path.stat().st_mtime_ns, path.name))


def split(source: Path, output_dir: Path, force: bool) -> list[Path]:
    output_dir.mkdir(parents=True, exist_ok=True)
    stamp = export_date(source)
    outputs = []

    for track in TRACKS:
        output = output_dir / f"{track.name}-{stamp}.ogg"
        outputs.append(output)
        if output.exists() and not force:
            raise SystemExit(f"output exists (use --force to replace): {output}")

        duration = track.end - track.start
        command = [
            "ffmpeg",
            "-hide_banner",
            "-loglevel",
            "error",
            "-y",
            "-i",
            str(source),
            "-ss",
            f"{track.start:.6f}",
            "-t",
            f"{duration:.6f}",
            "-map",
            "0:a:0",
            "-map_metadata",
            "0",
            "-c:a",
            "libvorbis",
            "-b:a",
            "192k",
            "-metadata",
            "artist=Terry Fail",
            "-metadata",
            f"title={track.name}-{stamp}",
            "-metadata",
            f"date={stamp}",
            str(output),
        ]
        print(
            f"{track.name:>5} {track.start:10.6f}..{track.end:10.6f} "
            f"({duration:9.6f}s) -> {output.name}"
        )
        subprocess.run(command, check=True)
        old_output = output_dir / f"{track.name}-{stamp}.flac"
        if old_output.exists():
            old_output.unlink()

    return outputs


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--source",
        type=Path,
        help="exported FLAC to split (default: newest FLAC in --source-dir)",
    )
    parser.add_argument(
        "--source-dir",
        type=Path,
        default=DEFAULT_SOURCE_DIR,
        help=f"Bitwig project directory (default: {DEFAULT_SOURCE_DIR})",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=DEFAULT_OUTPUT_DIR,
        help=f"where dated tracks go (default: {DEFAULT_OUTPUT_DIR})",
    )
    parser.add_argument("--force", action="store_true", help="replace existing dated tracks")
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    source = args.source or latest_export(args.source_dir)
    if not source.is_file():
        raise SystemExit(f"source is not a file: {source}")
    output_dir = args.output_dir
    print(f"source: {source}")
    print(f"date:   {export_date(source)}")
    print(f"gap:    257.623243..261.814263 (4.191020s, discarded)")
    split(source, output_dir, args.force)


if __name__ == "__main__":
    main()
