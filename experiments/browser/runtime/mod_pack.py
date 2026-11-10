"""Stage real mod UI content with decoded pixels; never include assemblies or credentials."""

import struct
import sys
import zipfile
from pathlib import Path

from PIL import Image

root, destination = map(Path, sys.argv[1:])
with zipfile.ZipFile(destination, 'w', compression=zipfile.ZIP_DEFLATED) as archive:
    archive.write(root / 'Defs' / 'KeyBindings.xml', 'Defs/KeyBindings.xml')
    for folder in ('About', 'Themes'):
        for path in sorted((root / folder).rglob('*')):
            if path.is_file() and path.suffix in ('.xml', '.toml'):
                archive.write(path, path.relative_to(root).as_posix())
    for path in sorted((root / 'Textures').rglob('*.png')):
        with Image.open(path) as source:
            pixels = source.convert('RGBA').transpose(Image.Transpose.FLIP_TOP_BOTTOM)
            archive.writestr(
                path.relative_to(root).with_suffix('.rgba').as_posix(),
                struct.pack('<II', *pixels.size) + pixels.tobytes(),
            )
