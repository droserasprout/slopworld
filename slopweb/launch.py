"""Launch the local browser workspace; game-derived data and secrets stay private."""

import argparse
import fcntl
import json
import os
import shutil
import subprocess
import sys
import tempfile
import uuid
from pathlib import Path

import aot
import application_paths
import cli
from appearance import preferences
from appearance import prepare_fonts
from appearance import write_preferences
from endpoint import connection
from timings import Timings

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


def prepare(
    bundle: Path,
    game: Path,
    cache: Path,
    rebuild: bool = False,
    profile: Path | None = None,
    timings: Timings | None = None,
) -> Path:
    """Compose immutable phase outputs under one preparation transaction lock."""
    timings = timings or Timings()
    appearance = preferences(profile) if profile is not None else preferences()
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
    font_settings: dict[str, object] = {
        name: appearance[name] for name in ('fontName', 'fontSize', 'uiFontName', 'uiFontSize') if name in appearance
    }
    # Bump a phase's version when its preparation ordering or output contract changes.
    contents: dict[Path, str] = {}
    keys = {
        'managed': aot.digest_manifest(managed_inputs, 'managed-v1', contents),
        'assets': aot.digest_manifest(assets_inputs, 'assets-v1', contents),
        'fonts': aot.digest_manifest(font_inputs, ['fonts-v1', str(game), font_settings], contents),
        'background': aot.digest_manifest(background_inputs, ['background-v1', str(game)], contents),
    }
    # Runtime composition includes authored assets, but not unrelated server code.
    assets = input_files(bundle, 'web', 'ost')
    assets.update(
        {str(p.relative_to(bundle)): p for p in (bundle / 'mod').rglob('*') if p.is_file() and p.suffix != '.dll'}
    )
    key = aot.digest_manifest(assets, [keys, appearance], contents)[:24]
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
            print('Reusing prepared runtime', flush=True)
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
                    with timings.step(f'Prepare {name}'):
                        build(root)
                    (root / '.ready').write_text(keys[name])
                    root.rename(component)
            else:
                print(f'Reusing prepared {name}', flush=True)
            components.append(component)
            component_pointers.append((pointer, component.name))
        if destination.exists():
            destination = cache / (key + '-' + uuid.uuid4().hex[:12])
        with timings.step('Compose runtime'), tempfile.TemporaryDirectory(prefix='.prepare-', dir=cache) as temporary:
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
    paths = application_paths.resolve(
        env, args.endpoint, getattr(args, 'profile', None), getattr(args, 'sidecar', False)
    )
    env['SLOPWORLD_PROFILE'] = str(paths.profile)
    env.pop('SLOPWEB_ENDPOINT_FILE', None)
    env.pop('SLOPWEB_TOKEN_FILE', None)
    if args.endpoint is not None:
        if args.token_file or getattr(args, 'url', None):
            raise ValueError('--endpoint cannot be combined with --url or --token-file')
        descriptor = paths.endpoint
    elif getattr(args, 'url', None) or args.token_file:
        if not getattr(args, 'url', None) or not args.token_file:
            raise ValueError('--url and --token-file must be supplied together')
        env.update(SLOPD_URL=args.url, SLOPWEB_TOKEN_FILE=str(Path(args.token_file).expanduser().resolve()))
        env.pop('SLOPD_TOKEN', None)
        connection(env)
        return env
    elif 'SLOPD_URL' in env:
        connection(env)
        return env
    else:
        descriptor = paths.endpoint
    env.pop('SLOPD_URL', None)
    env.pop('SLOPD_TOKEN', None)
    env['SLOPWEB_ENDPOINT_FILE'] = str(descriptor.resolve())
    connection(env)
    return env


def main() -> None:
    parser = cli.parser()
    args = parser.parse_args()
    if args.command == 'completions':
        print(cli.completion_script(args.shell), end='')
        return
    if args.command == 'paths':
        try:
            paths = application_paths.resolve(endpoint=args.endpoint, profile=args.profile, sidecar=args.sidecar)
            if args.name == 'game':
                selected = application_paths.game_directory(args.game)
            elif args.name == 'web-cache' and args.cache is not None:
                selected = application_paths.expand(args.cache, os.environ)
            else:
                selected = paths.query(args.name)
            print(selected)
        except (OSError, ValueError) as error:
            parser.exit(1, f'slopweb: {error}\n')
        return
    if not 1 <= args.port <= 65535:
        parser.error('--port must be between 1 and 65535')
    os.umask(0o077)
    bundle = Path(__file__).resolve().parent
    try:
        paths = application_paths.resolve(endpoint=args.endpoint, profile=args.profile, sidecar=args.sidecar)
        game = application_paths.game_directory(args.game)
        cache = application_paths.expand(args.cache, os.environ) if args.cache is not None else paths.web_cache
        env = environment(args) if not args.prepare else os.environ.copy()
        env['SLOPWORLD_PROFILE'] = str(paths.profile)
        timings = Timings()
        try:
            sdk = None
            if cli.use_aot(args):
                sdk_selection = args.aot_sdk or os.environ.get('SLOPWEB_AOT_SDK') or shutil.which('dotnet')
                if not sdk_selection:
                    raise ValueError('Mixed AOT requires .NET 10 SDK with wasm-tools; select --aot-sdk PATH')
                sdk = Path(sdk_selection)
                with timings.step('Validate AOT toolchain'):
                    aot.toolchain(sdk)
            with timings.step('Game preparation (including cache checks)'):
                root = prepare(bundle, game, cache, args.rebuild, paths.profile, timings)
            if sdk is not None:
                with timings.step('Mixed AOT publish and composition (or cache reuse)'):
                    root = aot.prepare(bundle, root, cache, sdk)
        finally:
            timings.summary('Runtime preparation summary')
        if args.prepare:
            print(f'Prepared local runtime: {root}')
            return
        env.pop('SLOPWEB_PUBLIC_ORIGIN', None)
        if args.public_origin:
            env['SLOPWEB_PUBLIC_ORIGIN'] = args.public_origin
        env.update(
            SLOPWEB_FONT_GAME=str(game),
            SLOPWEB_FONT_CACHE=str(cache / 'fonts' / root.name),
        )
        print(f'Open http://127.0.0.1:{args.port}{PAGE}', flush=True)
        os.execvpe('node', ['node', str(bundle / 'server/server.mjs'), str(root), str(args.port)], env)
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        parser.exit(1, f'slopweb: {error}\n')


if __name__ == '__main__':
    main()
