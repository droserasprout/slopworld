"""Stage source assets and canonical notices before the launcher's atomic mod install."""

import argparse
import tempfile
from pathlib import Path

from tools.release import source_mod
from tools.utils import run
from tools.utils import run_main


def install(runner: str, game: str) -> None:
    # Keep generated notices out of the checkout, including on staging/install failure.
    with tempfile.TemporaryDirectory(prefix='slopworld-mod-') as directory:
        source = Path(directory) / 'SlopWorld'
        source_mod.stage(source)
        run([runner, 'mod', 'install', '--source', str(source), '--game', game])


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--runner', required=True)
    parser.add_argument('--game', required=True)
    args = parser.parse_args()
    install(args.runner, args.game)


if __name__ == '__main__':
    run_main(main)
