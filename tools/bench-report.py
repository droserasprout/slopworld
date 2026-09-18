#!/usr/bin/env python3
"""Run the complete game-free benchmark suite three times and write an averaged note."""

from __future__ import annotations

import argparse
import csv
import shutil
import datetime as dt
import re
import subprocess
import sys
from collections import OrderedDict
from dataclasses import dataclass
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
RUNS = 3

DAEMON_LINE = re.compile(
    r"^(?P<name>.+?)\s+p50=(?P<p50>[0-9]+(?:\.[0-9]+)?)\s+"
    r"p95=(?P<p95>[0-9]+(?:\.[0-9]+)?)\s*$"
)
CSHARP_LINE = re.compile(
    r"^(?P<name>.+?)\s+(?P<p50>[0-9]+(?:\.[0-9]+)?)\s+"
    r"(?P<p95>[0-9]+(?:\.[0-9]+)?)\s+(?P<bytes>[0-9]+(?:\.[0-9]+)?)\s*$"
)


@dataclass
class Sample:
    p50: float
    p95: float
    bytes_per_op: float | None = None
    wire_bytes: float | None = None


def parse_samples(output: str) -> OrderedDict[str, Sample]:
    samples: OrderedDict[str, Sample] = OrderedDict()
    for line in output.splitlines():
        match = DAEMON_LINE.fullmatch(line) or CSHARP_LINE.fullmatch(line)
        if match is None:
            continue
        values = match.groupdict()
        samples[values["name"].strip()] = Sample(
            p50=float(values["p50"]),
            p95=float(values["p95"]),
            bytes_per_op=float(values["bytes"]) if values.get("bytes") else None,
        )
    return samples


def run_bench(build: str, run_number: int, raw_path: Path) -> OrderedDict[str, Sample]:
    command = ["make", f"BUILD={build}", "bench"]
    print(f"\n=== performance run {run_number}/{RUNS}: {' '.join(command)} ===", flush=True)
    process = subprocess.Popen(
        command,
        cwd=ROOT,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        bufsize=1,
    )
    assert process.stdout is not None
    output: list[str] = []
    for line in process.stdout:
        print(line, end="", flush=True)
        output.append(line)
    result = process.wait()
    if result != 0:
        raise RuntimeError(f"benchmark run {run_number} failed with exit status {result}")

    raw_path.mkdir(parents=True, exist_ok=True)
    (raw_path / f"run-{run_number}.log").write_text("".join(output))
    samples = parse_samples("".join(output))
    if not samples:
        raise RuntimeError(f"benchmark run {run_number} produced no daemon/C# measurements")
    for runtime in ("mono", "net8", "rust"):
        path = ROOT / "bench/ipc/results" / f"{runtime}.csv"
        shutil.copyfile(path, raw_path / f"{runtime}-{run_number}.csv")
        with path.open() as stream:
            rows = list(csv.DictReader(stream))
        if len(rows) != 16:
            raise RuntimeError(f"{runtime} IPC benchmark produced {len(rows)} rows, expected 16")
        for row in rows:
            name = f"IPC/{runtime}/{row['fixture']}/{row['lane']}"
            if name in samples:
                raise RuntimeError(f"duplicate benchmark: {name}")
            samples[name] = Sample(float(row["p50_us"]), float(row["p95_us"]),
                float(row["allocated_bytes"]) if "allocated_bytes" in row else None,
                float(row["wire_bytes"]))
    return samples


def average_runs(runs: list[OrderedDict[str, Sample]]) -> OrderedDict[str, Sample]:
    expected = list(runs[0])
    expected_set = set(expected)
    for index, run in enumerate(runs[1:], start=2):
        if set(run) != expected_set:
            missing = sorted(expected_set - set(run))
            extra = sorted(set(run) - expected_set)
            details = []
            if missing:
                details.append(f"missing: {', '.join(missing)}")
            if extra:
                details.append(f"unexpected: {', '.join(extra)}")
            raise RuntimeError(f"benchmark run {index} did not match run 1 ({'; '.join(details)})")

    averaged: OrderedDict[str, Sample] = OrderedDict()
    for name in expected:
        measurements = [run[name] for run in runs]
        if any(item.wire_bytes != measurements[0].wire_bytes for item in measurements):
            raise RuntimeError(f"wire size changed between runs: {name}")
        bytes_values = [item.bytes_per_op for item in measurements]
        averaged[name] = Sample(
            p50=sum(item.p50 for item in measurements) / len(measurements),
            p95=sum(item.p95 for item in measurements) / len(measurements),
            wire_bytes=measurements[0].wire_bytes,
            bytes_per_op=(
                sum(value for value in bytes_values if value is not None) / len(bytes_values)
                if all(value is not None for value in bytes_values)
                else None
            ),
        )
    return averaged


def commit_hash() -> str:
    return subprocess.check_output(
        ["git", "rev-parse", "HEAD"], cwd=ROOT, text=True
    ).strip()


def write_note(
    output_path: Path,
    build: str,
    revision: str,
    generated: dt.datetime,
    results: OrderedDict[str, Sample],
) -> None:
    lines = [
        "# Averaged performance suite",
        "",
        f"- Generated: {generated.isoformat(timespec='seconds')}",
        f"- Commit: `{revision}`",
        f"- Build: `{build}`",
        f"- Runs: {RUNS}",
        f"- Command: `make BUILD={build} bench`",
        "",
        "Each value is the arithmetic mean of the three complete suite runs. Timings are in "
        "microseconds; B/op is managed allocation per operation for the C# benchmarks.",
        "IPC p50/p95 describe batch averages, not individual-message tail latency. Burst8 is eight "
        "live frames including coalescing; wire bytes count the whole burst. Codec/queue measurements "
        "exclude network and rendering. Mono and .NET 8 are reported separately.",
        f"Raw run logs and IPC CSVs (local, ignored): `{output_path.with_suffix('.raw').name}/`.",
        "",
        "| Benchmark | p50 (µs) | p95 (µs) | B/op | Wire bytes |",
        "| --- | ---: | ---: | ---: | ---: |",
    ]
    for name, sample in results.items():
        escaped_name = name.replace("|", "\\|")
        bytes_value = f"{sample.bytes_per_op:.1f}" if sample.bytes_per_op is not None else "n/a"
        wire_value = f"{sample.wire_bytes:.0f}" if sample.wire_bytes is not None else "n/a"
        lines.append(f"| {escaped_name} | {sample.p50:.3f} | {sample.p95:.3f} | {bytes_value} | {wire_value} |")
    lines += ["", "## IPC comparison", "", "Ratios use the averaged p50 values above.", "",
              "| Runtime / fixture / operation | Speedup | Allocation reduction |",
              "| --- | ---: | ---: |"]
    for name, old in results.items():
        if not name.startswith("IPC/") or "/json-" not in name:
            continue
        new = results[name.replace("/json-", "/protobuf-")]
        reduction = (f"{100 * (1 - new.bytes_per_op / old.bytes_per_op):.1f}%"
                     if old.bytes_per_op and new.bytes_per_op is not None else "n/a")
        lines.append(f"| {name.removeprefix('IPC/').replace('/json-', '/')} | "
                     f"{old.p50 / new.p50:.2f}× | {reduction} |")
    lines.append("")
    output_path.write_text("\n".join(lines), encoding="utf-8")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--build",
        choices=("debug", "release"),
        default="release",
        help="benchmark build configuration (default: release)",
    )
    parser.add_argument(
        "--output",
        type=Path,
        help="note path (default: notes/perf-suite.md, replaced on each run)",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    revision = commit_hash()
    generated = dt.datetime.now(dt.timezone.utc)
    output_path = args.output or ROOT / "notes" / "perf-suite.md"
    if not output_path.is_absolute():
        output_path = ROOT / output_path
    if args.output and output_path.exists():
        print(f"refusing to overwrite existing note: {output_path}", file=sys.stderr)
        return 2

    try:
        runs = [run_bench(args.build, run_number, output_path.with_suffix(".raw")) for run_number in range(1, RUNS + 1)]
        results = average_runs(runs)
        write_note(output_path, args.build, revision, generated, results)
    except (OSError, RuntimeError, subprocess.CalledProcessError) as error:
        print(f"bench-report: {error}", file=sys.stderr)
        return 1

    try:
        display_path = output_path.relative_to(ROOT)
    except ValueError:
        display_path = output_path
    print(f"\nWrote averaged performance note: {display_path}", flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
