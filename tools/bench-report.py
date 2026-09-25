#!/usr/bin/env python3
"""Collect game-free benchmarks in shared CSVs or render a saved run."""

from __future__ import annotations

import argparse
import os
import platform
import shutil
from statistics import median
import csv
import datetime as dt
import re
import subprocess
import sys
from collections import OrderedDict
from dataclasses import dataclass
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "bench"))
import results_data as data
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
    p50_range: tuple[float, float] | None = None
    p95_range: tuple[float, float] | None = None
    bytes_range: tuple[float, float] | None = None


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


def run_bench(build: str, run_number: int, raw_path: Path, suite="gamefree") -> OrderedDict[str, Sample]:
    command = ["bash", "tools/bench.sh", "run", suite]
    print(f"\n=== performance run {run_number}: {' '.join(command)} ===", flush=True)
    repeat_path = raw_path / f"repeat-{run_number}"
    temporary = repeat_path / "tmp"
    temporary.mkdir(parents=True, exist_ok=True)
    # The Rust storage probes use std::env::temp_dir(). Keep their scratch
    # files inside this run, and retain them only if a benchmark fails.
    output: list[str] = []
    log_path = repeat_path / "gamefree.log"
    with log_path.open("x", encoding="utf-8") as log:
        process = subprocess.Popen(
            command,
            cwd=ROOT,
            env={**os.environ, "BUILD": build, "DOTNET": os.environ.get("DOTNET", "dotnet"),
                 "BENCH_IPC_OUTPUT": str(repeat_path / "ipc"), "TMPDIR": str(temporary)},
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            bufsize=1,
        )
        assert process.stdout is not None
        for line in process.stdout:
            print(line, end="", flush=True)
            log.write(line)
            log.flush()
            output.append(line)
        result = process.wait()
    if result != 0:
        raise RuntimeError(f"benchmark run {run_number} failed with exit status {result}; see {log_path}")

    samples = parse_samples("".join(output))
    if not samples and suite != "ipc":
        raise RuntimeError(f"benchmark run {run_number} produced no daemon/C# measurements")
    if suite not in ("gamefree", "ipc"):
        shutil.rmtree(temporary)
        return samples
    for runtime in ("mono", "net8", "rust"):
        path = repeat_path / "ipc" / f"{runtime}.csv"
        with path.open() as stream:
            rows = list(csv.DictReader(stream))
        lanes = ("protobuf-encode", "protobuf-decode") if runtime == "rust" else ("protobuf-receive", "protobuf-queue1", "protobuf-burst8")
        expected = {(fixture, lane) for fixture in ("plain", "ansi", "unicode", "large") for lane in lanes}
        actual = {(row["fixture"], row["lane"]) for row in rows}
        if len(rows) != len(expected) or actual != expected:
            raise RuntimeError(
                f"{runtime} IPC benchmark produced {len(rows)} rows, expected {len(expected)}; "
                f"missing: {sorted(expected - actual)}; unexpected: {sorted(actual - expected)}"
            )
        for row in rows:
            runtime_label = "coreclr" if runtime == "net8" else runtime
            name = f"IPC/{runtime_label}/{row['fixture']}/{row['lane']}"
            if name in samples:
                raise RuntimeError(f"duplicate benchmark: {name}")
            samples[name] = Sample(float(row["p50_us"]), float(row["p95_us"]),
                float(row["allocated_bytes"]) if "allocated_bytes" in row else None,
                float(row["wire_bytes"]))
    shutil.rmtree(temporary)
    return samples


def summarize_runs(runs: list[OrderedDict[str, Sample]]) -> OrderedDict[str, Sample]:
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

    summarized: OrderedDict[str, Sample] = OrderedDict()
    for name in expected:
        measurements = [run[name] for run in runs]
        if any(item.wire_bytes != measurements[0].wire_bytes for item in measurements):
            raise RuntimeError(f"wire size changed between runs: {name}")
        bytes_values = [item.bytes_per_op for item in measurements]
        summarized[name] = Sample(
            p50=median(item.p50 for item in measurements),
            p50_range=(min(item.p50 for item in measurements), max(item.p50 for item in measurements)),
            p95=median(item.p95 for item in measurements),
            p95_range=(min(item.p95 for item in measurements), max(item.p95 for item in measurements)),
            wire_bytes=measurements[0].wire_bytes,
            bytes_per_op=(
                median(value for value in bytes_values if value is not None)
                if all(value is not None for value in bytes_values)
                else None
            ),
            bytes_range=(min(bytes_values), max(bytes_values)) if all(value is not None for value in bytes_values) else None,
        )
    return summarized


def commit_hash() -> str:
    return subprocess.check_output(
        ["git", "rev-parse", "HEAD"], cwd=ROOT, text=True
    ).strip()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("run", "report"))
    parser.add_argument("--suite", choices=("gamefree", "daemon", "mod", "ipc"), default="gamefree")
    parser.add_argument("--run", help="directory name under bench/results (default: UTC timestamp for run)")
    parser.add_argument("--baseline", help="run name for a comparison report")
    parser.add_argument("--mode", choices=("absolute", "relative"), default="absolute")
    parser.add_argument("--latest", action="store_true", help="write a stable, dateless report at bench/latest-report.md")
    parser.add_argument("--fallback-run", help="for --latest, fill absent suite/phase data from a saved run")
    parser.add_argument("--repeats", type=int)
    parser.add_argument(
        "--build",
        choices=("debug", "release"),
        default="release",
        help="benchmark build configuration (default: release)",
    )
    parser.add_argument(
        "--output",
        type=Path,
        help="Markdown path (default: bench/results/<run>/report.md)",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    try:
        run_name = args.run or (data.default_run() if args.action == "run" else None)
        if not run_name:
            raise ValueError("--run is required for reports")
        if args.latest and (args.action != "report" or args.output or args.baseline or args.mode != "absolute"):
            raise ValueError("--latest requires a plain report action without output, baseline, or relative mode")
        if args.fallback_run and not args.latest:
            raise ValueError("--fallback-run requires --latest")
        directory = data.run_directory(run_name)
        if args.action == "run":
            if (directory / "raw" / "repeat-1").exists() or any(
                    row["suite"] != "terminal" for row in data.read(directory / "metrics.csv")):
                raise ValueError(f"run already has game-free measurements: {directory}")
            repeats = args.repeats or (RUNS if args.suite == "gamefree" else 1)
            if repeats < 1:
                raise ValueError("repeats must be positive")
            subprocess.run([os.environ.get("MAKE_CMD", "make"), f"BUILD={args.build}", "bench-build"], cwd=ROOT, check=True)
            directory.mkdir(parents=True, exist_ok=True)
            data.add_metadata(directory, "gamefree", "", {"revision": commit_hash(), "build": args.build,
                "started": dt.datetime.now(dt.timezone.utc).isoformat(), "suite": args.suite,
                "repeats": repeats, "system": platform.system(), "machine": platform.machine(),
                "host": platform.node(),
                "cpu_count": os.cpu_count()})
            runs = [run_bench(args.build, number, directory / "raw", args.suite)
                    for number in range(1, repeats + 1)]
            if repeats > 1:
                summarize_runs(runs)
            for number, samples in enumerate(runs, start=1):
                for name, sample in samples.items():
                    suite = "ipc" if name.startswith("IPC/") else ("mod" if sample.bytes_per_op is not None else "daemon")
                    for stat, value in (("p50", sample.p50), ("p95", sample.p95)):
                        data.add_metric(directory, suite, "", name, "duration", stat, "us", number, value)
                    if sample.bytes_per_op is not None:
                        data.add_metric(directory, suite, "", name, "allocation", "mean", "B/op", number, sample.bytes_per_op)
                    if sample.wire_bytes is not None:
                        data.add_metric(directory, suite, "", name, "wire_size", "total", "B", number, sample.wire_bytes)
            print(f"Results: {directory}")
        baseline = data.run_directory(args.baseline) if args.baseline else None
        output_path = ROOT / "bench/latest-report.md" if args.latest else args.output or directory / "report.md"
        fallback = data.run_directory(args.fallback_run) if args.fallback_run else None
        data.write_report(directory, output_path, baseline, args.mode, latest=args.latest,
                          fallback=fallback)
    except (OSError, RuntimeError, ValueError, subprocess.CalledProcessError) as error:
        print(f"bench-report: {error}", file=sys.stderr)
        return 1

    try:
        display_path = output_path.relative_to(ROOT)
    except ValueError:
        display_path = output_path
    print(f"\nWrote benchmark report: {display_path}", flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
