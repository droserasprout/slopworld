"""Launch the local browser workspace; game-derived data and secrets stay private."""

import argparse
import fcntl
import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile
import tomllib
import uuid
from pathlib import Path

PAGE = '/index.html?entry&assets&eco&workspace&live&app'


def prepare(bundle: Path, game: Path, cache: Path, rebuild: bool = False) -> Path:
    """Build an immutable private runtime; a failed rebuild preserves previous output."""
    inputs = [
        game / name for name in ('RimWorld.dll', 'Assembly-CSharp.dll', 'UnityStubs.dll', 'Waylith.dll', 'Assets.zip')
    ]
    identity = [(str(path.resolve()), path.stat().st_size, path.stat().st_mtime_ns) for path in inputs]
    identity.append(('bundle', (bundle / 'BUILD_ID').read_text(), 0))
    key = hashlib.sha256(json.dumps(identity).encode()).hexdigest()[:24]
    cache.mkdir(parents=True, exist_ok=True, mode=0o700)
    destination = cache / key
    with (cache / '.prepare.lock').open('w') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        active = cache / ('.active-' + key)
        if active.is_file():
            selected = active.read_text().strip()
            if Path(selected).name != selected or not selected.startswith(key):
                raise ValueError('Invalid private runtime cache pointer')
            destination = cache / selected
        if (destination / '.ready').is_file() and not rebuild:
            return destination
        # A forced refresh gets a new path so another running server keeps its files.
        if destination.exists():
            destination = cache / (key + '-' + uuid.uuid4().hex[:12])
        with tempfile.TemporaryDirectory(prefix='.prepare-', dir=cache) as temporary:
            root = Path(temporary) / 'runtime'
            shutil.copytree(bundle / 'web', root)
            shutil.copy2(bundle / 'Adapters.dll', root / 'Adapters.dll')
            (root / 'assets').mkdir()
            shutil.copy2(game / 'Assets.zip', root / 'assets/Assets.zip')
            run = lambda *args: subprocess.run([str(arg) for arg in args], check=True)
            run('dotnet', bundle / 'scanner/Scanner.dll', game, '--stage', root / 'game', '--jobs')
            run(sys.executable, bundle / 'core_pack.py', game, root / 'assets/Core.zip')
            run('dotnet', bundle / 'offline/Offline.dll', '--core', root, bundle / 'Probe.dll')
            shutil.copytree(bundle / 'mod', root / 'mod')
            run('dotnet', bundle / 'offline/Offline.dll', '--eco', root, bundle / 'mod/SlopWorld.dll')
            (root / '.ready').write_text(key + '\n')
            root.rename(destination)
            replacement = active.with_suffix('.tmp')
            replacement.write_text(destination.name + '\n')
            replacement.replace(active)
    return destination


def environment(args: argparse.Namespace) -> dict[str, str]:
    """Read the established endpoint descriptor only on the host."""
    env = os.environ.copy()
    config = Path(
        env.get('SLOPD_CONFIG_ROOT', str(Path(env.get('XDG_CONFIG_HOME', str(Path.home() / '.config'))) / 'slopworld'))
    ).expanduser()
    descriptor = Path(env.get('SLOPD_ENDPOINT', str(config / 'endpoints/local.toml'))).expanduser()
    values = tomllib.loads(descriptor.read_text()) if descriptor.is_file() else {}
    endpoint = args.endpoint or env.get('SLOPD_URL') or values.get('url')
    token = (
        Path(args.token_file).expanduser().read_text().strip()
        if args.token_file
        else env.get('SLOPD_TOKEN') or values.get('token')
    )
    if not endpoint or not token:
        raise ValueError('Configure SLOPD_URL and SLOPD_TOKEN, or start slopd to write its local endpoint descriptor.')
    env.update(SLOPD_URL=endpoint, SLOPD_TOKEN=token)
    return env


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        '--game',
        type=Path,
        default=Path(os.environ.get('RIMWORLD', str(Path.home() / '.local/share/Steam/steamapps/common/RimWorld'))),
    )
    parser.add_argument('--port', type=int, default=8787)
    parser.add_argument('--endpoint', help='Daemon HTTP URL (credentials remain on the host)')
    parser.add_argument('--token-file', help='Read a host-only daemon token file')
    parser.add_argument(
        '--cache', type=Path, default=Path(os.environ.get('XDG_CACHE_HOME', str(Path.home() / '.cache'))) / 'slopweb'
    )
    parser.add_argument('--prepare', action='store_true', help='Prepare local game assets and exit')
    parser.add_argument('--rebuild', action='store_true', help='Refresh local game data after a game update')
    args = parser.parse_args()
    if not 1 <= args.port <= 65535:
        parser.error('--port must be between 1 and 65535')
    os.umask(0o077)
    bundle = Path(__file__).resolve().parent
    try:
        env = environment(args) if not args.prepare else os.environ.copy()
        root = prepare(bundle, args.game.expanduser().resolve(), args.cache.expanduser().resolve(), args.rebuild)
        if args.prepare:
            print(f'Prepared local runtime: {root}')
            return
        print(f'Open http://127.0.0.1:{args.port}{PAGE}', flush=True)
        os.execvpe('node', ['node', str(bundle / 'server/server.mjs'), str(root), str(args.port)], env)
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        parser.exit(1, f'slopweb: {error}\n')


if __name__ == '__main__':
    main()
