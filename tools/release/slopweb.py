"""Stage redistributable browser tooling; never package installed game data or caches."""

import argparse
import hashlib
import json
import shutil
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

from tools import ROOT
from tools.release.staging import copy_file
from tools.utils import run
from tools.utils import run_main

OUTPUT = ROOT / 'slopweb/out'

# Reviewed authored inputs only. Never copy a compiler/build directory wholesale.
AOT_SOURCES = (
    'Program.cs',
    'BrowserInput.cs',
    'BrowserLifecycle.cs',
    'BrowserJobs.cs',
    'OfflineHook.cs',
    'BrowserDraws.cs',
    'BrowserTerrain.cs',
    'PackedTexture.cs',
    'BrowserScene.cs',
    'BrowserEntry.cs',
    'BrowserHub.cs',
    'BrowserWorkspace.cs',
    'BrowserGlyphCache.cs',
    'BrowserCursor.cs',
    'BrowserShortcutFixtures.cs',
)


def tracked_mod_files() -> set[str]:
    names = run(['git', 'ls-files', '-z', '--', 'mod'], cwd=ROOT, capture_output=True, text=True).stdout
    return {Path(name).relative_to('mod').as_posix() for name in names.split('\0') if name}


def validate_content(archive: Path, tracked: set[str]) -> None:
    with zipfile.ZipFile(archive) as content:
        for name in content.namelist():
            source = Path(name).with_suffix('.png').as_posix() if name.endswith('.rgba') else name
            if source not in tracked:
                raise ValueError(f'Package content is not a tracked mod asset: {source}')


def stage_aot_sources(runtime: Path, bundle: Path) -> None:
    """Ship the reviewed authored compile set, never neighboring private files."""
    declared = [item.get('Include', '') for item in ET.parse(runtime / 'Probe.csproj').findall('.//Compile')]
    if sorted(declared) != sorted(AOT_SOURCES):
        raise ValueError('Probe compiler sources changed; review the slopweb AOT source allowlist')
    for name in ('Probe.csproj', *AOT_SOURCES):
        source = runtime / name
        if source.is_symlink():
            raise ValueError(f'AOT compiler source must not be a symlink: {source}')
        copy_file(source, bundle / 'aot-src' / name)


def runtime_package(runtime: Path) -> Path:
    """Use the runtime pack resolved by this build, including SDK workload packs."""
    name = 'Microsoft.NETCore.App.Runtime.Mono.browser-wasm'
    manifest = runtime / 'obj/Debug/net10.0/staticwebassets.build.json'
    if manifest.is_file():
        roots = set()
        for asset in json.loads(manifest.read_text())['Assets']:
            source = Path(asset.get('OriginalItemSpec', ''))
            if source.name == 'dotnet.native.wasm' and len(source.parents) > 4:
                package = source.parents[3]
                if package.parent.name.lower() == name.lower():
                    roots.add(package)
        if len(roots) == 1:
            return roots.pop()
        if roots:
            raise ValueError('Build resolved multiple browser runtime packs')
    assets = json.loads((runtime / 'obj/project.assets.json').read_text())
    for framework in assets['project']['frameworks'].values():
        for dependency in framework.get('downloadDependencies', []):
            if dependency['name'] != name:
                continue
            version = str(dependency['version']).strip('[]').split(',')[0].strip()
            for folder in assets['packageFolders']:
                package = Path(folder) / name.lower() / version
                if package.is_dir():
                    return package
    raise ValueError('Cannot locate the browser runtime pack used by this build')


def build(destination: Path = OUTPUT) -> None:
    runtime = ROOT / 'slopweb/runtime'
    destination.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='.slopweb-', dir=destination.parent) as temporary:
        bundle = Path(temporary) / 'bundle'
        bundle.mkdir()
        for source, target in (
            (ROOT / 'slopweb/launch.py', 'launch.py'),
            (ROOT / 'slopweb/aot.py', 'aot.py'),
            (ROOT / 'slopweb/appearance.py', 'appearance.py'),
            (ROOT / 'slopweb/fonts.py', 'fonts.py'),
            (ROOT / 'slopweb/portraits.py', 'portraits.py'),
            (runtime / 'core_pack.py', 'core_pack.py'),
            (runtime / 'bin/Debug/net10.0/Probe.dll', 'Probe.dll'),
            (runtime / 'Adapters/bin/Debug/net10.0/Adapters.dll', 'Adapters.dll'),
        ):
            copy_file(source, bundle / target)
        stage_aot_sources(runtime, bundle)
        # FontProbe's build directory also contains installed game references.
        # Ship only our executable and its runtime metadata, never that directory.
        for helper, directory in (('FontProbe', 'font-probe'), ('BackgroundProbe', 'background-probe')):
            for suffix in ('.dll', '.deps.json', '.runtimeconfig.json'):
                copy_file(
                    runtime / helper / 'bin/Debug/net10.0' / (helper + suffix),
                    bundle / directory / (helper + suffix),
                )
        # These are unmodified build outputs of our projects, NOT staged game roots.
        shutil.copytree(runtime / 'bin/Debug/net10.0/wwwroot', bundle / 'web')
        for path in (runtime / 'wwwroot').iterdir():
            if path.is_file():
                copy_file(path, bundle / 'web' / path.name)
        for name in ('local-fonts.mjs', 'local-fonts.wasm', 'local-fonts.LICENSE.txt'):
            if not (bundle / 'web' / name).is_file():
                raise ValueError('Missing local font rasterizer; run just browser-local-fonts-build')
        for source, target in (
            (runtime / 'Scanner/bin/Debug/net10.0', 'scanner'),
            (ROOT / 'slopweb/offline/bin/Debug/net10.0', 'offline'),
        ):
            for path in source.iterdir():
                if path.suffix in ('.dll', '.json'):
                    copy_file(path, bundle / target / path.name)
        for name in ('SlopWorld', '0Harmony', 'Google.Protobuf', 'Markdig', 'AngleSharp', 'Tomlyn'):
            copy_file(ROOT / 'mod/Assemblies' / f'{name}.dll', bundle / 'mod' / f'{name}.dll')
        # Content.zip contains exclusively authored mod UI assets, never Core/game assets.
        content = bundle / 'mod/Content.zip'
        content.parent.mkdir(parents=True, exist_ok=True)
        run([sys.executable, str(runtime / 'mod_pack.py'), str(ROOT / 'mod'), str(content)])
        tracked = tracked_mod_files()
        validate_content(content, tracked)
        # Only authored SlopWorld music is shipped; game audio stays in private runtime data.
        for name in sorted(tracked):
            if Path(name).parent == Path('Sounds/SlopWorld/OST') and Path(name).suffix == '.ogg':
                copy_file(ROOT / 'mod' / name, bundle / 'ost' / Path(name).name)
        copy_file(ROOT / 'mod/Textures/SlopWorld/LoadingFont.png', bundle / 'mod/LoadingFont.png')
        copy_file(ROOT / 'shared/loading-tips.txt', bundle / 'mod/loading-tips.txt')
        for name in ('server.mjs', 'bridge.mjs', 'websocket.mjs', 'fonts.mjs'):
            copy_file(ROOT / 'slopweb/server' / name, bundle / 'server' / name)
        package = runtime_package(runtime)
        for name in ('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT'):
            copy_file(package / name, bundle / 'licenses/dotnet' / name)
        digest = hashlib.sha256()
        for path in sorted(bundle.rglob('*')):
            if path.is_file():
                digest.update(path.relative_to(bundle).as_posix().encode())
                with path.open('rb') as stream:
                    for chunk in iter(lambda: stream.read(1024 * 1024), b''):
                        digest.update(chunk)
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
