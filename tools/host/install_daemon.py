"""Install the user daemon and restart only when its running binary or effective unit differs."""

import filecmp
import os
import tempfile
from pathlib import Path

from tools import ROOT
from tools.utils import log
from tools.utils import run
from tools.utils import run_main


def service_contents(source: Path) -> bytes:
    contents = source.read_bytes()
    # User services do not inherit the installer's diagnostic overrides.
    if 'SLOPWORLD_DEBUG' in os.environ:
        value = os.environ['SLOPWORLD_DEBUG']
        if value.lower() not in ('0', '1', 'true', 'false', ''):
            raise ValueError('SLOPWORLD_DEBUG must be 0, 1, true, false, or empty')
        contents += f'\n[Service]\nEnvironment=SLOPWORLD_DEBUG={value}\n'.encode()
    return contents


def needs_restart(binary: Path, unit: Path, contents: bytes) -> bool:
    active = run(['systemctl', '--user', 'is-active', '--quiet', 'slopd.service'], check=False)
    if active.returncode:
        return True
    result = run(
        ['systemctl', '--user', 'show', '--property=MainPID', '--value', 'slopd.service'],
        capture_output=True,
        text=True,
    )
    try:
        pid = int(result.stdout.strip())
        # Compare the running inode, even when a previous install replaced its path.
        return not (
            pid > 0 and filecmp.cmp(binary, f'/proc/{pid}/exe', shallow=False) and unit.read_bytes() == contents
        )
    except (OSError, ValueError):
        return True


def main() -> None:
    target = ROOT / os.environ['TARGET']
    binaries = Path(os.environ['BIN'])
    units = Path(os.environ['UNITS'])
    contents = service_contents(ROOT / 'slopd/slopd.service')
    restart = needs_restart(target / 'slopd', units / 'slopd.service', contents)
    if not restart:
        log(
            f'slopd already runs the latest {os.environ["BUILD"]} build and service unit. '
            'The daemon does not need a restart.'
        )
    run(['install', '-Dm755', str(target / 'slopd'), str(binaries / 'slopd')])
    run(['install', '-Dm755', str(target / 'slopctl'), str(binaries / 'slopctl')])
    with tempfile.NamedTemporaryFile() as unit:
        unit.write(contents)
        unit.flush()
        run(['install', '-Dm644', unit.name, str(units / 'slopd.service')])
    for arguments in (['daemon-reload'], ['enable', '--now', 'slopd.service']):
        run(['systemctl', '--user', *arguments])
    if restart:
        run(['systemctl', '--user', 'restart', 'slopd.service'])
    status = run(
        ['systemctl', '--user', '--no-pager', 'status', 'slopd.service'], capture_output=True, text=True, check=False
    )
    log('\n'.join(status.stdout.splitlines()[:3]))


if __name__ == '__main__':
    run_main(main)
