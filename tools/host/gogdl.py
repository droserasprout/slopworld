"""Log in, install, or update native Linux RimWorld with the configured gogdl command."""

import argparse
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

from tools.utils import command
from tools.utils import log
from tools.utils import run
from tools.utils import run_main


def has_credentials(auth: Path) -> bool:
    return auth.is_file() and auth.stat().st_size > 0


def login(auth: Path, gogdl: list[str]) -> None:
    auth.parent.mkdir(parents=True, exist_ok=True)
    url = os.environ['GOGDL_LOGIN_URL']
    if browser := shutil.which('xdg-open'):
        run([browser, url], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
    log('Log into GOG in your browser, then paste the authorization code here.')
    log(f'If the browser did not open, visit: {url}')
    print('Authorization code: ', end='', file=sys.stderr, flush=True)
    code = sys.stdin.readline().rstrip('\r\n')
    if not code:
        raise ValueError('authorization code is empty')
    # gogdl prints tokens on success and can return zero for a rejected code.
    # Capture both streams and never include its response in diagnostics.
    result = run(gogdl + ['auth', '--code', code], capture_output=True, text=True, check=False)
    if result.returncode:
        raise ValueError(f'gogdl authentication failed with exit status {result.returncode}')
    try:
        response = json.loads(result.stdout)
    except (TypeError, ValueError):
        raise ValueError('gogdl returned an invalid authentication response') from None
    if (
        not isinstance(response, dict)
        or response.get('error')
        or not response.get('access_token')
        or not response.get('refresh_token')
    ):
        raise ValueError('gogdl authentication failed')
    if not has_credentials(auth):
        raise ValueError(f'gogdl did not save credentials to {auth}')


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=('login', 'install', 'update'))
    args = parser.parse_args()
    auth = Path(os.environ['GOGDL_AUTH'])
    gogdl = command('GOGDL', 'gogdl') + ['--auth-config-path', str(auth)]
    if args.action == 'login':
        login(auth, gogdl)
        return
    game = Path(os.environ['RIMWORLD']) if args.action == 'update' else Path(os.environ['GOGDL_PATH'])
    if args.action == 'update' and not os.access(game / 'RimWorldLinux', os.X_OK):
        raise ValueError(f'RimWorld is missing at {game}. Run just gogdl-install or set RIMWORLD.')
    if not has_credentials(auth):
        raise ValueError(f'The gogdl login is missing at {auth}. Run just gogdl-login.')
    if args.action == 'install':
        game.mkdir(parents=True, exist_ok=True)
    run(
        gogdl
        + [
            'download' if args.action == 'install' else 'update',
            os.environ['GOGDL_ID'],
            '--path',
            str(game),
            '--platform',
            'linux',
            '--with-dlcs',
        ],
    )


if __name__ == '__main__':
    run_main(main)
