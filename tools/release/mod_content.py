"""Stage real mod UI content with decoded pixels; never include assemblies or credentials."""

import struct
import zipfile
from pathlib import Path

from PIL import Image


def write_member(archive: zipfile.ZipFile, name: str, data: bytes) -> None:
    # Runtime identity hashes archive bytes. Wall clock, source mtimes and modes
    # must not invalidate an otherwise identical content publication.
    info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
    info.create_system = 3
    info.external_attr = 0o100644 << 16
    info.compress_type = zipfile.ZIP_DEFLATED
    archive.writestr(info, data)


def pack_mod_content(root: Path, destination: Path) -> None:
    with zipfile.ZipFile(destination, 'w', compression=zipfile.ZIP_DEFLATED) as archive:
        write_member(archive, 'Defs/KeyBindings.xml', (root / 'Defs' / 'KeyBindings.xml').read_bytes())
        write_member(archive, 'Defs/PlannerBench.xml', (root / 'Defs' / 'PlannerBench.xml').read_bytes())
        write_member(archive, 'Defs/Jukebox.xml', (root / 'Defs' / 'Jukebox.xml').read_bytes())
        for folder in ('About', 'Themes'):
            for path in sorted((root / folder).rglob('*')):
                if path.is_file() and path.suffix in ('.xml', '.toml'):
                    write_member(archive, path.relative_to(root).as_posix(), path.read_bytes())
        for path in sorted((root / 'Textures').rglob('*.png')):
            with Image.open(path) as source:
                pixels = source.convert('RGBA').transpose(Image.Transpose.FLIP_TOP_BOTTOM)
                write_member(
                    archive,
                    path.relative_to(root).with_suffix('.rgba').as_posix(),
                    struct.pack('<II', *pixels.size) + pixels.tobytes(),
                )
