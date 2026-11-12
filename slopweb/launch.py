"""Launch the local browser workspace; game-derived data and secrets stay private."""

import argparse
import fcntl
import json
import os
import shutil
import subprocess
import sys
import tempfile
import tomllib
import uuid
from pathlib import Path

import aot
from appearance import preferences
from appearance import prepare_fonts
from appearance import write_preferences

PAGE = '/'


def input_files(root: Path, *names: str) -> dict[str, Path]:
    """Expand explicit input owners without including neighboring caches."""
    result = {}
    for name in names:
        path = root / name
        if path.is_dir():
            result.update({str(p.relative_to(root)): p for p in path.rglob('*') if p.is_file()})
        elif path.is_file():
            result[name] = path
    return result


def prepare(bundle: Path, game: Path, cache: Path, rebuild: bool = False) -> Path:
    """Compose immutable phase outputs under one preparation transaction lock."""
    appearance = preferences()
    game_dlls = {p.name: p for p in game.glob('*.dll')}
    native = {p.name: p for p in game.glob('*.so*') if p.is_file()}
    managed_inputs = input_files(bundle, 'scanner', 'offline', 'Probe.dll', 'Adapters.dll')
    managed_inputs.update({str(p.relative_to(bundle)): p for p in (bundle / 'mod').glob('*.dll')})
    managed_inputs.update({f'game/{name}': path for name, path in game_dlls.items()})
    assets_inputs = input_files(game, 'Assets.zip', 'Data/Core', 'Rendering/Shaders')
    assets_inputs.update({f'tool/{name}': path for name, path in input_files(bundle, 'core_pack.py').items()})
    font_inputs = input_files(bundle, 'font-probe', 'appearance.py', 'mod/SlopWorld.dll')
    background_inputs = input_files(bundle, 'background-probe', 'mod/SlopWorld.dll')
    for inputs in (font_inputs, background_inputs):
        inputs.update({f'game/{name}': path for name, path in (game_dlls | native).items()})
    background_inputs.update(input_files(game, 'Assets.zip'))
    font_settings = {
        name: appearance[name] for name in ('fontName', 'fontSize', 'uiFontName', 'uiFontSize') if name in appearance
    }
    # Bump a phase's version when its preparation ordering or output contract changes.
    keys = {
        'managed': aot.digest_files(managed_inputs, 'managed-v1'),
        'assets': aot.digest_files(assets_inputs, 'assets-v1'),
        'fonts': aot.digest_files(font_inputs, ['fonts-v1', str(game), font_settings]),
        'background': aot.digest_files(background_inputs, ['background-v1', str(game)]),
    }
    # Runtime composition includes authored assets, but not unrelated server code.
    assets = input_files(bundle, 'web', 'ost')
    assets.update(
        {str(p.relative_to(bundle)): p for p in (bundle / 'mod').rglob('*') if p.is_file() and p.suffix != '.dll'}
    )
    key = aot.digest_files(assets, [keys, appearance])[:24]
    cache.mkdir(parents=True, exist_ok=True, mode=0o700)
    with (cache / '.prepare.lock').open('a') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        active = cache / ('.active-' + key)
        destination = cache / key
        if active.is_file():
            selected = active.read_text().strip()
            if Path(selected).name != selected or not selected.startswith(key):
                raise ValueError('Invalid private runtime cache pointer')
            destination = cache / selected
        if (destination / '.ready').is_file() and not rebuild:
            return destination

        def run(*args: object) -> None:
            subprocess.run([str(arg) for arg in args], check=True)

        def managed(root: Path) -> None:
            shutil.copy2(bundle / 'Adapters.dll', root / 'Adapters.dll')
            run('dotnet', bundle / 'scanner/Scanner.dll', game, '--stage', root / 'game', '--jobs')
            run('dotnet', bundle / 'offline/Offline.dll', '--core', root, bundle / 'Probe.dll')
            (root / 'mod').mkdir(exist_ok=True)
            for path in (bundle / 'mod').glob('*.dll'):
                shutil.copy2(path, root / 'mod' / path.name)
            run('dotnet', bundle / 'offline/Offline.dll', '--eco', root, bundle / 'mod/SlopWorld.dll')

        def game_assets(root: Path) -> None:
            (root / 'assets').mkdir()
            shutil.copy2(game / 'Assets.zip', root / 'assets/Assets.zip')
            run(sys.executable, bundle / 'core_pack.py', game, root / 'assets/Core.zip')

        def fonts(root: Path) -> None:
            prepare_fonts(bundle, game, root, font_settings)

        def background(root: Path) -> None:
            run(
                'dotnet',
                bundle / 'background-probe/BackgroundProbe.dll',
                game,
                bundle / 'mod/SlopWorld.dll',
                root / 'assets/background',
            )

        components = []
        component_pointers = []
        for name, build in (
            ('managed', managed),
            ('assets', game_assets),
            ('fonts', fonts),
            ('background', background),
        ):
            parent = cache / 'prepared-components' / name
            parent.mkdir(parents=True, exist_ok=True)
            pointer = parent / ('.active-' + keys[name])
            component = parent / keys[name]
            if pointer.is_file():
                selected = pointer.read_text().strip()
                if Path(selected).name != selected or not selected.startswith(keys[name]):
                    raise ValueError('Invalid preparation component pointer')
                component = parent / selected
            if rebuild:
                component = parent / (keys[name] + '-' + uuid.uuid4().hex[:12])
            if not (component / '.ready').is_file():
                with tempfile.TemporaryDirectory(prefix='.prepare-', dir=parent) as temporary:
                    root = Path(temporary) / 'runtime'
                    root.mkdir()
                    build(root)
                    (root / '.ready').write_text(keys[name])
                    root.rename(component)
            components.append(component)
            component_pointers.append((pointer, component.name))
        if destination.exists():
            destination = cache / (key + '-' + uuid.uuid4().hex[:12])
        with tempfile.TemporaryDirectory(prefix='.prepare-', dir=cache) as temporary:
            root = Path(temporary) / 'runtime'
            shutil.copytree(bundle / 'web', root)
            for component in components:
                shutil.copytree(component, root, dirs_exist_ok=True, ignore=shutil.ignore_patterns('.ready'))
            for name, path in assets.items():
                if name.startswith('mod/'):
                    target = root / name
                    target.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copy2(path, target)
            shutil.copytree(bundle / 'ost', root / 'assets/ost')
            # Preserve the resolved Auto fallback from font preparation.
            resolved = components[2] / 'assets/reference-appearance.json'
            values = dict(appearance)
            if resolved.is_file():
                values.update(json.loads(resolved.read_text()))
            write_preferences(root, values)
            (root / '.ready').write_text(key + '\n')
            root.rename(destination)
            for pointer, selected in component_pointers:
                pending = pointer.with_suffix('.tmp')
                pending.write_text(selected)
                pending.replace(pointer)
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
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument('--interpreted', action='store_true', help='Use the interpreter instead of default mixed AOT')
    mode.add_argument(
        '--aot-sdk',
        type=Path,
        help='Select the .NET 10 SDK with wasm-tools (default: SLOPWEB_AOT_SDK or dotnet on PATH)',
    )
    args = parser.parse_args()
    if not 1 <= args.port <= 65535:
        parser.error('--port must be between 1 and 65535')
    os.umask(0o077)
    bundle = Path(__file__).resolve().parent
    try:
        env = environment(args) if not args.prepare else os.environ.copy()
        root = prepare(bundle, args.game.expanduser().resolve(), args.cache.expanduser().resolve(), args.rebuild)
        if not args.interpreted:
            sdk = args.aot_sdk or os.environ.get('SLOPWEB_AOT_SDK') or shutil.which('dotnet')
            if not sdk:
                raise ValueError(
                    'Mixed AOT requires .NET 10 with wasm-tools; select --aot-sdk PATH or use --interpreted'
                )
            root = aot.prepare(bundle, root, args.cache.expanduser().resolve(), Path(sdk))
        if args.prepare:
            print(f'Prepared local runtime: {root}')
            return
        env.update(
            SLOPWEB_FONT_GAME=str(args.game.expanduser().resolve()),
            SLOPWEB_FONT_CACHE=str(args.cache.expanduser().resolve() / 'fonts' / root.name),
        )
        print(f'Open http://127.0.0.1:{args.port}{PAGE}', flush=True)
        os.execvpe('node', ['node', str(bundle / 'server/server.mjs'), str(root), str(args.port)], env)
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        parser.exit(1, f'slopweb: {error}\n')


if __name__ == '__main__':
    main()
