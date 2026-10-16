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
    sidecar = os.environ['SLOPCAR']
    # Failed preparation returns to the loop before replacing the running sidecar.
    for command in (('sidecar-image',), ('install-mod',)):
        if run([just, *command], cwd=ROOT, check=False).returncode:
            return
    run([sidecar, 'rm'], cwd=ROOT, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
    if run([sidecar, 'start', *arguments], cwd=ROOT, check=False).returncode:
        return
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
