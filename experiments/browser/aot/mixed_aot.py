"""Private mixed-AOT snapshots; never prepare game IL or change launcher output."""

import argparse
import fcntl
import json
import shutil
import subprocess
import sys
from contextlib import contextmanager
from datetime import UTC
from datetime import datetime
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(REPO / 'slopweb'))
import aot as compiler

PACKAGES = compiler.PACKAGES
INTERPRETED = compiler.INTERPRETED


def private_output(output: Path) -> Path:
    output = output.resolve()
    private = REPO / 'experiments/browser/artifacts'
    if output == private or private not in output.parents:
        raise ValueError('Output must be a new child of experiments/browser/artifacts')
    return output


def checked_sdk(sdk: Path) -> Path:
    sdk = sdk.resolve(strict=True)
    if not sdk.is_file() or sdk.name != 'dotnet':
        raise ValueError('Pass the existing wasm-tools SDK dotnet executable')
    return sdk


def prepare(prepared: Path, output: Path, sdk: Path) -> None:
    sdk = checked_sdk(sdk)
    prepared = prepared.resolve(strict=True)
    output = private_output(output)
    if output == prepared or prepared in output.parents or output in prepared.parents:
        raise ValueError('Prepared root and output must be disjoint')
    if output.exists():
        raise FileExistsError(output)
    dlls = compiler.assemblies(prepared)
    main = compiler.published_loader((prepared / 'main.js').read_text())

    # Exclusive creation reserves output. On failure keep diagnostics; never delete a caller path.
    output.parent.mkdir(parents=True, exist_ok=True)
    output.mkdir()
    shutil.copytree(prepared, output / 'snapshot')
    (output / 'snapshot/main.js').write_text(main)
    candidate = output / 'candidate'
    compiler.write_candidate(REPO / 'slopweb/runtime', candidate, dlls)
    (output / 'experiment.json').write_text(json.dumps({'sdk': str(sdk), 'prepared_root': str(prepared)}, indent=2))


@contextmanager
def publish_lock(output: Path):
    # Keep the lock file inode stable. Closing releases flock even on exceptions.
    with (output / 'publish.lock').open('a') as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError as error:
            raise RuntimeError('Publish or stage is already active for this output') from error
        yield lock


def publish(output: Path, sdk: Path) -> None:
    output = private_output(output)
    sdk = checked_sdk(sdk)
    manifest = json.loads((output / 'experiment.json').read_text())
    if manifest['sdk'] != str(sdk):
        raise ValueError('Publish must use the SDK selected during prepare')
    with publish_lock(output) as lock, (output / 'publish.log').open('a') as log:
        (output / 'publish.complete').unlink(missing_ok=True)
        log.write(f'\n--- Publish attempt {datetime.now(UTC).isoformat()} ---\n')
        log.flush()
        subprocess.run(
            [
                str(sdk),
                'publish',
                str(output / 'candidate/Probe.csproj'),
                '-c',
                'Release',
                '-o',
                str(output / 'published'),
                '--nologo',
                '-v',
                'minimal',
                '-p:RestoreLockedMode=false',
                f'-bl:{output / "publish.binlog"}',
            ],
            cwd=output / 'candidate',
            stdout=log,
            stderr=subprocess.STDOUT,
            check=True,
            # An orphaned compiler retains the lock until it exits too.
            pass_fds=(lock.fileno(),),
        )
        (output / 'publish.complete').touch()


def stage(output: Path) -> None:
    output = private_output(output)
    with publish_lock(output):
        if not (output / 'publish.complete').is_file():
            raise ValueError('Publish must finish successfully before staging')
        published = output / 'published/wwwroot'
        if not (published / '_framework/dotnet.js').is_file() or not list(
            (published / '_framework').glob('dotnet.native*.wasm')
        ):
            raise ValueError('Published framework is incomplete')
        destination = output / 'wwwroot'
        destination.mkdir()  # Refuse to merge with an old runtime.
        shutil.copytree(output / 'snapshot', destination, dirs_exist_ok=True)
        shutil.rmtree(destination / '_framework')
        shutil.copytree(published, destination, dirs_exist_ok=True)
        shutil.copy2(output / 'snapshot/main.js', destination / 'main.js')


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='action', required=True)
    for action in ('prepare', 'publish', 'stage'):
        command = sub.add_parser(action)
        command.add_argument('--output', type=Path, required=True)
        if action != 'stage':
            command.add_argument('--sdk', type=Path, required=True)
        if action == 'prepare':
            command.add_argument('--prepared-root', type=Path, required=True)
    args = parser.parse_args()
    if args.action == 'prepare':
        prepare(args.prepared_root, args.output, args.sdk)
    elif args.action == 'publish':
        publish(args.output, args.sdk)
    else:
        stage(args.output)


if __name__ == '__main__':
    main()
