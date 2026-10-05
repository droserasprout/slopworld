"""Build benchmark binaries once and run selected suites with just's tool settings."""

import argparse
import os
import shlex
from pathlib import Path

from tools import ROOT
from tools.utils import command
from tools.utils import run
from tools.utils import run_main


def configuration() -> str:
    return 'Release' if os.environ['BUILD'] == 'release' else 'Debug'


def rust_binary(directory: Path, name: str) -> Path:
    return directory / os.environ.get('CARGO_TARGET_DIR', 'target') / os.environ['BUILD'] / name


def build_ipc() -> None:
    run(
        command('DOTNET', 'dotnet')
        + [
            'build',
            'bench/ipc/csharp/IpcBench.csproj',
            '--configuration',
            configuration(),
            '--verbosity',
            'quiet',
            '-p:RestoreLockedMode=true',
        ],
        cwd=ROOT,
    )
    run(
        command('CARGO', 'cargo') + ['build', '--quiet', *shlex.split(os.environ.get('CARGOFLAGS', ''))],
        cwd=ROOT / 'bench/ipc/rust',
    )


def run_ipc() -> None:
    output = (ROOT / os.environ.get('BENCH_IPC_OUTPUT', 'bench/results/adhoc-ipc/raw/ipc')).resolve()
    output.mkdir(parents=True, exist_ok=True)
    # Rust writes round-trip files beside fixtures. Keep them in the run scratch
    # directory; the reporting driver removes scratch only after successful runs.
    fixtures = (Path(os.environ.get('TMPDIR', str(output / 'tmp'))) / 'ipc-fixtures').resolve()
    fixtures.mkdir(parents=True, exist_ok=True)
    for name in ('plain', 'ansi', 'unicode', 'large'):
        with (ROOT / f'bench/ipc/fixtures/{name}.textproto').open('rb') as source:
            with (fixtures / f'{name}.pb').open('wb') as destination:
                run(
                    ['protoc', '-I', 'shared', '--encode=slopworld.Event', 'shared/slopworld.proto'],
                    cwd=ROOT,
                    stdin=source,
                    stdout=destination,
                )
    csharp = ROOT / 'bench/ipc/csharp/bin' / configuration()
    mono = ['mono', str(csharp / 'net472/IpcBench.exe')]
    runtimes = (
        ('mono', mono),
        ('net8', command('DOTNET', 'dotnet') + [str(csharp / 'net8.0/IpcBench.dll')]),
        ('rust', [str(rust_binary(ROOT / 'bench/ipc/rust', 'slopworld-ipc-bench'))]),
    )
    for runtime, arguments in runtimes:
        with (output / f'{runtime}.csv').open('w') as stream:
            run(
                arguments + [str(fixtures)],
                cwd=ROOT / 'bench/ipc/rust' if runtime == 'rust' else ROOT,
                env={**os.environ, 'DOTNET_TieredCompilation': '0'},
                stdout=stream,
            )
    run(mono + ['--verify', str(fixtures)], cwd=ROOT)


def build() -> None:
    run(
        command('CARGO', 'cargo')
        + ['build', '--quiet', '--bin', 'slopd', *shlex.split(os.environ.get('CARGOFLAGS', ''))],
        cwd=ROOT / 'slopd',
    )
    run(
        command('DOTNET', 'dotnet')
        + ['build', os.environ['TEST_PROJECT'], '--configuration', configuration(), '--verbosity', 'quiet'],
        cwd=ROOT,
    )
    build_ipc()


def run_suite(suite: str) -> None:
    if suite in ('gamefree', 'daemon'):
        run([str(rust_binary(ROOT / 'slopd', 'slopd')), '--perf-bench'], cwd=ROOT / 'slopd')
    if suite in ('gamefree', 'mod'):
        run(
            command('DOTNET', 'dotnet')
            + [f'mod/Tests/bin/{configuration()}/net8.0/SlopWorld.Tests.dll', '--perf-bench'],
            cwd=ROOT,
            env={**os.environ, 'DOTNET_TieredCompilation': '0'},
        )
    if suite in ('gamefree', 'ipc'):
        run_ipc()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=('build', 'run'))
    parser.add_argument('suite', choices=('gamefree', 'daemon', 'mod', 'ipc'), nargs='?', default='gamefree')
    args = parser.parse_args()
    if args.action == 'build':
        build_ipc() if args.suite == 'ipc' else build()
    else:
        run_suite(args.suite)


if __name__ == '__main__':
    run_main(main)
