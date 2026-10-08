"""Own distribution allowlists and file staging, independent of package formats and publication.

Release mods select tracked assets; source snapshots select whole asset directories
in source_mod. Both use these runtime DLL and asset-directory allowlists.
"""

import shutil
from pathlib import Path

from tools import ROOT
from tools.utils import run

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
