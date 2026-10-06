"""Collect Rust or C# coverage using the settings exported by just."""

import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import TextIO

from tools import ROOT
from tools.utils import command
from tools.utils import log
from tools.utils import run
from tools.utils import run_main


def summarize(path: Path, label: str) -> None:
    run([sys.executable, '-m', 'tools.coverage.coverage_summary', str(path), label], cwd=ROOT)


def daemon(output: Path) -> None:
    if not shutil.which('cargo-llvm-cov'):
        raise ValueError('Missing cargo-llvm-cov. Install it with: cargo install cargo-llvm-cov --locked')
    llvm_cov = shutil.which('llvm-cov')
    llvm_profdata = shutil.which('llvm-profdata')
    if not llvm_cov or not llvm_profdata:
        raise ValueError('Missing LLVM coverage tools.')
    exclusions = ['--ignore-filename-regex', os.environ['RUST_COVERAGE_EXCLUDE']]
    output.mkdir(parents=True, exist_ok=True)
    environment = {**os.environ, 'LLVM_COV': llvm_cov, 'LLVM_PROFDATA': llvm_profdata}
    cargo = command('CARGO', 'cargo') + ['llvm-cov']

    def cargo_run(*args: str, stdout: TextIO | None = None) -> None:
        run(cargo + list(args), cwd=ROOT / 'slopd', env=environment, stdout=stdout)

    cargo_run('clean', '--profraw-only')
    cargo_run('--no-report')
    report = output / 'rust.cobertura.xml'
    filtered = output / 'rust.filtered.cobertura.xml'
    cargo_run('report', '--cobertura', '--output-path', str(report))
    cargo_run('report', *exclusions, '--cobertura', '--output-path', str(filtered))
    with (output / 'rust.files.txt').open('w') as stream:
        cargo_run('report', *exclusions, stdout=stream)
    summarize(report, 'Rust (default scope)')
    summarize(filtered, 'Rust (excluding test/generated/benchmark files)')
    log(f'Per-file coverage: {output / "rust.files.txt"}')


def mod(output: Path) -> None:
    dotnet = command('DOTNET', 'dotnet')
    run(dotnet + ['tool', 'restore'], cwd=ROOT)
    output.mkdir(parents=True, exist_ok=True)
    run(
        dotnet
        + [
            'build',
            os.environ['TEST_PROJECT'],
            '--configuration',
            'Release',
            '-p:Coverage=true',
            '-p:RestoreLockedMode=true',
        ],
        cwd=ROOT,
    )
    report = output / 'csharp.cobertura.xml'
    # Measure handwritten behavior, excluding tests and protoc serialization.
    run(
        dotnet
        + [
            'tool',
            'run',
            'coverlet',
            '--',
            os.environ['TEST_DLL'],
            '--target',
            dotnet[0],
            '--targetargs',
            subprocess.list2cmdline(dotnet[1:] + [os.environ['TEST_DLL'], '--quiet']),
            '--include-test-assembly',
            '--exclude-by-file',
            '**/mod/Tests/**/*.cs',
            '--exclude-by-file',
            '**/Client/Generated/Slopworld.cs',
            '--format',
            'cobertura',
            '--output',
            str(report),
        ],
        cwd=ROOT,
    )
    summarize(report, 'C#')


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('scope', choices=('daemon', 'mod'))
    args = parser.parse_args()
    output = (ROOT / os.environ['COVERAGE_DIR']).resolve()
    {'daemon': daemon, 'mod': mod}[args.scope](output)


if __name__ == '__main__':
    run_main(main)
