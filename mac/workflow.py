"""Check the native macOS install and orchestrate its existing sidecar."""

import argparse
import os
import platform
import shutil
import subprocess
from pathlib import Path

from tools import ROOT
from tools.utils import command
from tools.utils import log
from tools.utils import run
from tools.utils import run_main


def check(scope: str) -> None:
    if scope == 'mod-check':
        if not (Path(os.environ['MAC_MODS']) / 'SlopWorld/About/About.xml').is_file():
            raise ValueError('The SlopWorld mod is missing. Run just --justfile mac/justfile install.')
        return
    if platform.system() != 'Darwin':
        raise ValueError('macOS target requires Darwin')
    if scope == 'docker-check':
        if not shutil.which('docker'):
            raise ValueError('Docker is missing. Run just --justfile mac/justfile setup.')
        result = run(['docker', 'info'], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
        if result.returncode:
            raise ValueError('Docker Desktop is not running. Open Docker and retry.')
        return
    dotnet = command('DOTNET', 'dotnet')[0]
    if not shutil.which(dotnet):
        raise ValueError(f'{dotnet} is missing. Run just --justfile mac/justfile setup.')
    game = Path(os.environ['MAC_GAME'])
    if not os.access(game, os.X_OK):
        raise ValueError(f'The native RimWorld executable is missing: {game}. Set MAC_RIMWORLD or MAC_GAME.')
    managed = Path(os.environ['MAC_MANAGED'])
    if not (managed / 'Assembly-CSharp.dll').is_file():
        raise ValueError(f'missing RimWorld assemblies under {managed}')
    mods = Path(os.environ['MAC_MODS'])
    if not mods.is_dir():
        raise ValueError(f'The RimWorld Mods directory is missing: {mods}. Set MAC_RIMWORLD.')


def start(arguments: list[str]) -> None:
    container = os.environ['SLOPCAR_CONTAINER']
    inspect = ['docker', 'container', 'inspect']
    state = run(inspect + ['--format', '{{.State.Running}}', container], capture_output=True, text=True, check=False)
    if state.returncode == 0 and state.stdout.strip() == 'true':
        log(f'sidecar {container} is already running')
        return
    existing = run(inspect + [container], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
    run([os.environ['SLOPCAR'], 'start', *(arguments if existing.returncode else [])], cwd=ROOT)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=('all', 'start', 'docker-check', 'game-check', 'mod-check'))
    args, remainder = parser.parse_known_args()
    if args.action == 'start':
        start(remainder)
    elif remainder:
        parser.error('unexpected arguments: ' + ' '.join(remainder))
    elif args.action == 'all':
        just = [os.environ['JUST_CMD'], '--justfile', str(ROOT / 'mac/justfile')]
        # Keep separate invocations: install must finish before run's checks.
        run(just + ['install'], cwd=ROOT)
        run(just + ['run'], cwd=ROOT)
    else:
        check(args.action)


if __name__ == '__main__':
    run_main(main)
