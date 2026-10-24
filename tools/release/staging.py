"""Own distribution allowlists, common system payloads, notices, and file permissions.

Release mods select tracked assets; source snapshots select whole asset directories
in source_mod. Both use these runtime DLL and asset-directory allowlists.
"""

import shutil
from pathlib import Path

from tools import ROOT
from tools import cli_completions
from tools.utils import run

BINARIES = ('slopd', 'slopctl', 'slopworld')
MOD_DIRECTORIES = ('About', 'Defs', 'Patches', 'Sounds', 'Textures', 'Themes')
MOD_ASSEMBLIES = (
    'SlopWorld',
    '0Harmony',
    'Google.Protobuf',
    'AngleSharp',
    'Markdig',
    'Newtonsoft.Json',
    'Tomlyn',
    'System.Buffers',
    'System.Memory',
    'System.Numerics.Vectors',
    'System.Runtime.CompilerServices.Unsafe',
    'System.Text.Encoding.CodePages',
)


def copy_file(source: Path, destination: Path) -> None:
    if not source.is_file() or source.stat().st_size == 0:
        raise ValueError(f'Missing or empty release input: {source}')
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, destination)


def metadata(directory: Path, revision: str, version: str) -> None:
    (directory / 'REVISION').write_text(revision + '\n')
    (directory / 'VERSION').write_text(version + '\n')


def stage_mod(mod: Path, revision: str, version: str) -> None:
    """Stage only distributable mod assets and runtime assemblies for all packagers."""
    tracked = run(['git', 'ls-files', '-z', '--', 'mod'], cwd=ROOT, capture_output=True, text=True).stdout.split('\0')
    for name in filter(None, tracked):
        relative = Path(name).relative_to('mod')
        if relative.parts[0] in MOD_DIRECTORIES:
            copy_file(ROOT / name, mod / relative)
    for assembly in MOD_ASSEMBLIES:
        copy_file(ROOT / 'mod/Assemblies' / f'{assembly}.dll', mod / 'Assemblies' / f'{assembly}.dll')
    copy_file(ROOT / 'LICENSE', mod / 'About/LICENSE')
    shutil.copytree(ROOT / 'licenses', mod / 'About/ThirdPartyNotices')
    metadata(mod, revision, version)


def stage_system(payload: Path, binary_dir: Path) -> None:
    """Stage the system files shared by all native package formats."""
    for binary in BINARIES:
        target = payload / 'usr/bin' / binary
        copy_file(binary_dir / binary, target)
        target.chmod(0o755)
    cli_completions.stage(ROOT / 'packaging/completions', payload / 'usr/share', ('slopctl', 'slopworld'))
    unit = payload / 'usr/lib/systemd/user/slopd.service'
    copy_file(ROOT / 'slopd/slopd.service', unit)
    unit.write_text(unit.read_text().replace('%h/.local/bin/slopd', '/usr/bin/slopd'))
    for source, destination in (
        ('packaging/slopworld.desktop', 'usr/share/applications/slopworld.desktop'),
        ('mod/Textures/SlopWorld/SlopWorld_icon.png', 'usr/share/icons/hicolor/128x128/apps/slopworld.png'),
    ):
        copy_file(ROOT / source, payload / destination)


def stage_notices(payload: Path, copyright_path: str = 'usr/share/licenses/slopworld/LICENSE') -> None:
    destination = payload / copyright_path
    copy_file(ROOT / 'LICENSE', destination)
    shutil.copytree(ROOT / 'licenses', destination.parent / 'third-party')


def normalize_permissions(directory: Path, binaries: Path) -> None:
    """Make distributable files independent of source modes and the build umask."""
    for path in (directory, *directory.rglob('*')):
        path.chmod(0o755 if path.is_dir() or path.parent == binaries else 0o644)
