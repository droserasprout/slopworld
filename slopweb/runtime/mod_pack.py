"""Stage real mod UI content with decoded pixels; never include assemblies or credentials."""

import shutil
import struct
import sys
import zipfile
from pathlib import Path

from PIL import Image

root, destination = map(Path, sys.argv[1:])
# This authored atlas must be available before WASM or Content.zip is loaded.
shutil.copyfile(root / 'Textures/SlopWorld/LoadingFont.png', destination.parent / 'LoadingFont.png')
shutil.copyfile(root.parent / 'shared/loading-tips.txt', destination.parent / 'loading-tips.txt')
with zipfile.ZipFile(destination, 'w', compression=zipfile.ZIP_DEFLATED) as archive:
    archive.write(root / 'Defs' / 'KeyBindings.xml', 'Defs/KeyBindings.xml')
    archive.write(root / 'Defs' / 'PlannerBench.xml', 'Defs/PlannerBench.xml')
    archive.write(root / 'Defs' / 'Jukebox.xml', 'Defs/Jukebox.xml')
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
