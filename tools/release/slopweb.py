"""Stage redistributable browser tooling; never package installed game data or caches."""

import argparse
import hashlib
import json
import os
import shutil
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

from tools import ROOT
from tools import cli_completions
from tools.release.staging import copy_file
from tools.release.staging import metadata
from tools.utils import run
from tools.utils import run_main
from tools.version.metadata import package_version
from tools.version.resolve import from_git

OUTPUT = ROOT / 'slopweb/out'
OWNED = (
    'bin/slopweb',
    'share/slopweb',
    'share/doc/slopweb',
    'share/licenses/slopweb',
    *(str(Path('share') / cli_completions.relative_path(shell, 'slopweb')) for shell in cli_completions.SHELLS),
)

# Reviewed authored inputs only. Never copy a compiler/build directory wholesale.
WEB_ASSETS = (
    'asset-archive.js',
    'async-glyphs.js',
    'bootstrap.js',
    'clipboard.js',
    'cursor-image.js',
    'font-delivery.js',
    'glyph-storage.js',
    'glyph-worker.js',
    'gui-frame-reuse.js',
    'gui-input.js',
    'gui-pixels.js',
    'gui-quads.js',
    'gui-tint.js',
    'gui.js',
    'host-fonts.js',
    'index.html',
    'input.js',
    'loading-background.js',
    'loading-quotes.js',
    'loading.js',
    'local-fonts.LICENSE.txt',
    'local-fonts.mjs',
    'local-fonts.wasm',
    'main.js',
    'startup-assemblies.js',
    'startup-yield.js',
    'text-input.js',
    'workspace-audio.js',
    'workspace-cursor.js',
    'workspace-diagnostics.js',
    'workspace-fonts.js',
    'workspace-fullscreen.js',
    'workspace-keybindings.js',
    'workspace-links.js',
    'workspace-preferences.js',
    'workspace-rasterizer.js',
    'workspace-scheduler.js',
    'workspace-viewport.js',
    'workspace.html',
    'workspace.js',
)
HELPER_FILES = {
    'Scanner': ('Scanner.dll', 'Scanner.deps.json', 'Scanner.runtimeconfig.json'),
    'Offline': (
        'Offline.dll',
        'Offline.deps.json',
        'Offline.runtimeconfig.json',
        'Mono.Cecil.dll',
        'Mono.Cecil.Mdb.dll',
        'Mono.Cecil.Pdb.dll',
        'Mono.Cecil.Rocks.dll',
    ),
}
AOT_SOURCES = (
    'Program.cs',
    'BrowserInput.cs',
    'BrowserLifecycle.cs',
    'BrowserMod.cs',
    'BrowserEntry.cs',
    'BrowserWorkspace.cs',
    'BrowserGlyphCache.cs',
    'BrowserCursor.cs',
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


def build_identity() -> tuple[str, str]:
    version = os.environ.get('VERSION') or from_git(package_version(ROOT), ROOT)
    revision = run(['git', 'rev-parse', 'HEAD'], cwd=ROOT, capture_output=True, text=True).stdout.strip()
    return revision, version


def bundle_identity(bundle: Path) -> str:
    digest = hashlib.sha256()
    for path in sorted(bundle.rglob('*')):
        if path.is_file() and path != bundle / 'BUILD_ID':
            digest.update(path.relative_to(bundle).as_posix().encode())
            with path.open('rb') as stream:
                for chunk in iter(lambda: stream.read(1024 * 1024), b''):
                    digest.update(chunk)
    return digest.hexdigest() + '\n'


def build(destination: Path = OUTPUT) -> None:
    runtime = ROOT / 'slopweb/runtime'
    destination.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='.slopweb-', dir=destination.parent) as temporary:
        bundle = Path(temporary) / 'bundle'
        bundle.mkdir()
        for source, target in (
            (ROOT / 'slopweb/host/target/release/slopweb', 'slopweb-host'),
            (runtime / 'bin/Debug/net10.0/Probe.dll', 'Probe.dll'),
            (runtime / 'Adapters/bin/Debug/net10.0/Adapters.dll', 'Adapters.dll'),
        ):
            copy_file(source, bundle / target)
        (bundle / 'slopweb-host').chmod(0o755)
        stage_aot_sources(runtime, bundle)
        # FontProbe's build directory also contains installed game references.
        # Ship only our executable and its runtime metadata, never that directory.
        for helper, directory in (('FontProbe', 'font-probe'), ('BackgroundProbe', 'background-probe')):
            for suffix in ('.dll', '.deps.json', '.runtimeconfig.json'):
                copy_file(
                    runtime / helper / 'bin/Debug/net10.0' / (helper + suffix),
                    bundle / directory / (helper + suffix),
                )
        manifest = json.loads((runtime / 'obj/Debug/net10.0/staticwebassets.build.endpoints.json').read_text())
        framework = {item['AssetFile'] for item in manifest['Endpoints'] if item['AssetFile'].startswith('_framework/')}
        if '_framework/dotnet.js' not in framework:
            raise ValueError('Browser build manifest is missing the .NET loader')
        for name in sorted(framework):
            if '..' in Path(name).parts:
                raise ValueError(f'Invalid framework asset path: {name}')
            copy_file(runtime / 'bin/Debug/net10.0/wwwroot' / name, bundle / 'web' / name)
        for name in WEB_ASSETS:
            copy_file(runtime / 'wwwroot' / name, bundle / 'web' / name)
        copy_file(ROOT / 'docs/theme/favicon.png', bundle / 'web/favicon.png')
        for helper, source in [('Scanner', runtime / 'Scanner'), ('Offline', ROOT / 'slopweb/offline')]:
            for name in HELPER_FILES[helper]:
                copy_file(source / 'bin/Debug/net10.0' / name, bundle / helper.lower() / name)
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
        package = runtime_package(runtime)
        for name in ('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT'):
            copy_file(package / name, bundle / 'licenses/dotnet' / name)
        metadata(bundle, *build_identity())
        (bundle / 'BUILD_ID').write_text(bundle_identity(bundle))
        if destination.exists():
            shutil.rmtree(destination)
        bundle.rename(destination)


def stage(payload: Path, revision: str, version: str, bundle: Path = OUTPUT) -> None:
    if not (bundle / 'BUILD_ID').is_file():
        raise ValueError('Missing slopweb build; run just web-build')
    installed = payload / 'usr/share/slopweb'
    shutil.copytree(bundle, installed)
    metadata(installed, revision, version)
    # Distribution containers compile the native host against their own libc and
    # architecture. Managed/WASM assets remain the reviewed prebuilt bundle.
    if native := os.environ.get('SLOPWEB_HOST_BINARY'):
        copy_file(Path(native), installed / 'slopweb-host')
    (installed / 'slopweb-host').chmod(0o755)
    (installed / 'BUILD_ID').write_text(bundle_identity(installed))
    copy_file(ROOT / 'slopweb/slopweb', payload / 'usr/bin/slopweb')
    (payload / 'usr/bin/slopweb').chmod(0o755)
    cli_completions.stage(ROOT / 'packaging/completions', payload / 'usr/share', ('slopweb',))
    documentation = payload / 'usr/share/doc/slopweb'
    copy_file(ROOT / 'slopweb/README.md', documentation / 'README.md')
    metadata(documentation, revision, version)
    copy_file(ROOT / 'LICENSE', payload / 'usr/share/licenses/slopweb/LICENSE')
    shutil.copytree(ROOT / 'licenses', payload / 'usr/share/licenses/slopweb/third-party')


def validate_owned_paths(prefix: Path, *, uninstall: bool) -> None:
    # Check every owner before any mutation. Uninstall may unlink an owned leaf
    # symlink, but must never traverse one in the prefix or an owner's parents.
    for candidate in (prefix, *prefix.parents):
        if candidate.is_symlink():
            raise ValueError(f'Refusing to traverse symlink: {candidate}')
    for relative in OWNED:
        path = prefix / relative
        candidates = path.parents if uninstall else (path, *path.parents)
        for candidate in candidates:
            if candidate.is_symlink():
                raise ValueError(f'Refusing to traverse symlink: {candidate}')


def install(prefix: Path, uninstall: bool = False) -> None:
    # Install owns these paths only. Private generated caches and daemon config
    # are independent and deliberately survive uninstall.
    owned = OWNED
    prefix = prefix.absolute()
    validate_owned_paths(prefix, uninstall=uninstall)
    if uninstall:
        for relative in owned:
            path = prefix / relative
            if path.is_symlink() or path.is_file():
                path.unlink()
            elif path.is_dir():
                shutil.rmtree(path)
        return
    # Prepare on the destination filesystem before touching any installed path.
    # Rename whole owned trees so removed bundle files cannot survive an upgrade.
    prefix.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='.slopweb-install-', dir=prefix) as temporary:
        payload = Path(temporary)
        stage(payload, (OUTPUT / 'REVISION').read_text().strip(), (OUTPUT / 'VERSION').read_text().strip())
        validate_owned_paths(prefix, uninstall=False)
        for relative in owned:
            destination = prefix / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
        changed: list[tuple[Path, Path | None]] = []
        try:
            for relative in owned:
                source, destination = payload / 'usr' / relative, prefix / relative
                backup = payload / 'previous' / relative if destination.exists() else None
                if backup is not None:
                    backup.parent.mkdir(parents=True, exist_ok=True)
                    destination.rename(backup)
                changed.append((destination, backup))
                source.rename(destination)
        except BaseException:
            # A failed handoff restores every previously installed owner, including
            # the one whose new source could not be published.
            for destination, backup in reversed(changed):
                if destination.is_dir():
                    shutil.rmtree(destination)
                else:
                    destination.unlink(missing_ok=True)
                if backup is not None:
                    backup.rename(destination)
            raise


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=('build', 'install', 'uninstall'))
    parser.add_argument('--prefix', type=Path, default=Path.home() / '.local')
    args = parser.parse_args()
    if args.command == 'build':
        build()
    else:
        install(args.prefix.expanduser(), args.command == 'uninstall')


if __name__ == '__main__':
    run_main(main)
