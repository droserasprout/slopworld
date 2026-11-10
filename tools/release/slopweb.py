"""Stage redistributable browser tooling; never package installed game data or caches."""

import argparse
import hashlib
import json
import shutil
import tempfile
import zipfile
from pathlib import Path

from tools import ROOT
from tools.release.staging import copy_file
from tools.utils import run
from tools.utils import run_main

OUTPUT = ROOT / 'slopweb/out'


def tracked_mod_files() -> set[str]:
    names = run(['git', 'ls-files', '-z', '--', 'mod'], cwd=ROOT, capture_output=True, text=True).stdout
    return {Path(name).relative_to('mod').as_posix() for name in names.split('\0') if name}


def validate_content(archive: Path, tracked: set[str]) -> None:
    with zipfile.ZipFile(archive) as content:
        for name in content.namelist():
            source = Path(name).with_suffix('.png').as_posix() if name.endswith('.rgba') else name
            if source not in tracked:
                raise ValueError(f'Package content is not a tracked mod asset: {source}')


def build(destination: Path = OUTPUT) -> None:
    runtime = ROOT / 'experiments/browser/runtime'
    destination.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='.slopweb-', dir=destination.parent) as temporary:
        bundle = Path(temporary) / 'bundle'
        bundle.mkdir()
        for source, target in (
            (ROOT / 'slopweb/launch.py', 'launch.py'),
            (runtime / 'core_pack.py', 'core_pack.py'),
            (runtime / 'bin/Debug/net10.0/Probe.dll', 'Probe.dll'),
            (runtime / 'Adapters/bin/Debug/net10.0/Adapters.dll', 'Adapters.dll'),
        ):
            copy_file(source, bundle / target)
        # These are unmodified build outputs of our projects, NOT staged game roots.
        shutil.copytree(runtime / 'bin/Debug/net10.0/wwwroot', bundle / 'web')
        for path in (runtime / 'wwwroot').iterdir():
            if path.is_file():
                copy_file(path, bundle / 'web' / path.name)
        for source, target in (
            (runtime / 'Scanner/bin/Debug/net10.0', 'scanner'),
            (ROOT / 'experiments/browser/compat/Offline/bin/Debug/net10.0', 'offline'),
        ):
            for path in source.iterdir():
                if path.suffix in ('.dll', '.json'):
                    copy_file(path, bundle / target / path.name)
        for name in ('SlopWorld', '0Harmony', 'Google.Protobuf', 'Markdig', 'AngleSharp', 'Tomlyn'):
            copy_file(ROOT / 'mod/Assemblies' / f'{name}.dll', bundle / 'mod' / f'{name}.dll')
        # Content.zip contains exclusively authored mod UI assets, never Core/game assets.
        content = runtime / 'out/wwwroot-eco/mod/Content.zip'
        validate_content(content, tracked_mod_files())
        copy_file(content, bundle / 'mod/Content.zip')
        for name in ('server.mjs', 'bridge.mjs', 'websocket.mjs'):
            copy_file(ROOT / 'experiments/browser/harness' / name, bundle / 'server' / name)
        assets = json.loads((runtime / 'obj/project.assets.json').read_text())
        dependency = next(
            item
            for framework in assets['project']['frameworks'].values()
            for item in framework['downloadDependencies']
            if item['name'] == 'Microsoft.NETCore.App.Runtime.Mono.browser-wasm'
        )
        version = dependency['version'].strip('[]').split(',')[0].strip()
        package = next(
            Path(folder) / dependency['name'].lower() / version
            for folder in assets['packageFolders']
            if (Path(folder) / dependency['name'].lower() / version).is_dir()
        )
        for name in ('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT'):
            copy_file(package / name, bundle / 'licenses/dotnet' / name)
        digest = hashlib.sha256()
        for path in sorted(bundle.rglob('*')):
            if path.is_file():
                digest.update(path.relative_to(bundle).as_posix().encode())
                digest.update(path.read_bytes())
        (bundle / 'BUILD_ID').write_text(digest.hexdigest() + '\n')
        if destination.exists():
            shutil.rmtree(destination)
        bundle.rename(destination)


def stage(payload: Path, revision: str, version: str, bundle: Path = OUTPUT) -> None:
    if not (bundle / 'BUILD_ID').is_file():
        raise ValueError('Missing slopweb build; run just slopweb-build')
    shutil.copytree(bundle, payload / 'usr/share/slopweb')
    copy_file(ROOT / 'slopweb/slopweb', payload / 'usr/bin/slopweb')
    (payload / 'usr/bin/slopweb').chmod(0o755)
    documentation = payload / 'usr/share/doc/slopweb'
    copy_file(ROOT / 'slopweb/README.md', documentation / 'README.md')
    (documentation / 'VERSION').write_text(version + '\n')
    (documentation / 'REVISION').write_text(revision + '\n')
    copy_file(ROOT / 'LICENSE', payload / 'usr/share/licenses/slopweb/LICENSE')
    shutil.copytree(ROOT / 'licenses', payload / 'usr/share/licenses/slopweb/third-party')


def install(prefix: Path, uninstall: bool = False) -> None:
    # Install owns these paths only. Private generated caches and daemon config
    # are independent and deliberately survive uninstall.
    owned = ('bin/slopweb', 'share/slopweb', 'share/doc/slopweb', 'share/licenses/slopweb')
    if uninstall:
        for relative in owned:
            path = prefix / relative
            if path.is_symlink() or path.is_file():
                path.unlink()
            elif path.is_dir():
                shutil.rmtree(path)
        return
    with tempfile.TemporaryDirectory(prefix='slopweb-install-') as temporary:
        payload = Path(temporary)
        stage(payload, 'source', 'local')
        for relative in owned:
            source, destination = payload / 'usr' / relative, prefix / relative
            if destination.is_symlink():
                raise ValueError(f'Refusing to replace symlink: {destination}')
            destination.parent.mkdir(parents=True, exist_ok=True)
            if source.is_dir():
                shutil.copytree(source, destination, dirs_exist_ok=True)
            else:
                shutil.copy2(source, destination)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=('build', 'install', 'uninstall'))
    parser.add_argument('--prefix', type=Path, default=Path.home() / '.local')
    args = parser.parse_args()
    if args.command == 'build':
        build()
    else:
        install(args.prefix.expanduser().resolve(), args.command == 'uninstall')


if __name__ == '__main__':
    run_main(main)
