#!/usr/bin/env python3
"""Repeat the focused suite and summarize measured medians, preserving raw runs."""
import csv
import os
import platform
import shutil
import statistics
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
os.chdir(ROOT)
results = HERE / 'results'
results.mkdir(exist_ok=True)
metadata = [platform.platform(), subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip()]
for command in [['mono', '--version'], ['dotnet', '--version'], ['rustc', '--version'], ['protoc', '--version'], ['lscpu']]:
    metadata.append(subprocess.check_output(command, text=True).strip())
(results / 'environment.txt').write_text('\n\n'.join(metadata) + '\n')
data = {}
for run in range(1, 4):
    subprocess.run(['make', 'bench-ipc'], check=True)
    for runtime in ['mono', 'net8', 'rust']:
        path = results / f'{runtime}.csv'
        shutil.copyfile(path, results / f'{runtime}-{run}.csv')
        for row in csv.DictReader(path.open()):
            data.setdefault((runtime, row['fixture'], row['lane']), []).append(row)
def median(runtime, fixture, lane, field='p50_us'):
    return statistics.median(float(row[field]) for row in data[runtime, fixture, lane])
lines = ['# Protobuf IPC results', '', 'Three serial runs; medians of run p50 batch averages. See [methodology](README.md)', 'and [raw measurements](results/). Times are microseconds per operation; burst means eight frames.', '', '| Runtime / operation | Fixture | JSON µs | Protobuf µs | Speedup |', '| --- | --- | ---: | ---: | ---: |']
for runtime, operations in [('mono', ['receive', 'burst8']), ('net8', ['receive', 'burst8']), ('rust', ['encode', 'decode'])]:
    for operation in operations:
        for fixture in ['plain', 'ansi', 'unicode', 'large']:
            old, new = [median(runtime, fixture, f'{codec}-{operation}') for codec in ['json', 'protobuf']]
            lines.append(f'| {runtime} {operation} | {fixture} | {old:.2f} | {new:.2f} | {old/new:.2f}× |')
lines += ['', '| Fixture | JSON bytes | Protobuf bytes | Mono receive allocation reduction | Mono burst allocation reduction |', '| --- | ---: | ---: | ---: | ---: |']
for fixture in ['plain', 'ansi', 'unicode', 'large']:
    old, new = [median('mono', fixture, f'{codec}-receive', 'wire_bytes') for codec in ['json', 'protobuf']]
    reduction = [100 * (1 - median('mono', fixture, f'protobuf-{op}', 'allocated_bytes') / median('mono', fixture, f'json-{op}', 'allocated_bytes')) for op in ['receive', 'burst8']]
    lines.append(f'| {fixture} | {old:.0f} | {new:.0f} | {reduction[0]:.1f}% | {reduction[1]:.1f}% |')
lines += ['', 'The gain is primarily parsing cost, not compression: plain terminal text dominates payload size.', 'The burst lane includes decoding frames later discarded by coalescing; it is the more conservative', 'comparison for busy terminals. No network, game rendering or cold HTTP handler timing is included.', '', 'The implementation removes the production C# token tree, JSON envelope scanner and manual JSON', 'request builders. Generated messages serve both HTTP and WebSockets. Cold Rust handlers retain', 'in-memory Serde domain projections to preserve validation; hot screens convert directly. This', 'adds a schema/compiler and five Mono runtime DLLs, while keeping one binary contract and avoiding', 'a dual-protocol compatibility layer. Daemon, CLI and mod must be upgraded together.', '']
(HERE / 'REPORT.md').write_text('\n'.join(lines))
