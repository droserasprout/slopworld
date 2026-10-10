"""Rebuild and replace the configured sidecar before each game launch."""

import os
import signal
import subprocess
import sys
import time

from tools import ROOT
from tools.utils import run
from tools.utils import run_main


def iteration(arguments: list[str]) -> None:
    just = os.environ['JUST_CMD']
    for setting in ('SLOPCAR_CONFIG_DIR', 'SLOPCAR_DATA_DIR', 'SLOPCAR_PORT', 'SLOPCAR_CONTAINER'):
        if not os.environ.get(setting):
            raise ValueError(f'{setting} is required')
    sidecar = os.environ['SLOPCAR']
    # Like the interactive loop, failed commands return to the next iteration.
    run([just, 'sidecar-build'], cwd=ROOT, check=False)
    run([just, 'install-mod'], cwd=ROOT, check=False)
    run([sidecar, 'rm'], cwd=ROOT, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
    run([sidecar, 'start', *arguments], cwd=ROOT, check=False)
    run([just, 'sidecar-run'], cwd=ROOT, check=False)


def stop(*_) -> None:
    raise SystemExit(143)


def main() -> None:
    signal.signal(signal.SIGTERM, stop)
    while True:
        iteration(sys.argv[1:])
        time.sleep(1)


if __name__ == '__main__':
    run_main(main)
