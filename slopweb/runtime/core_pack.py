"""Stage Core data and shaders; generated archive is private ignored output."""

import sys
import zipfile
from pathlib import Path

game, destination = map(Path, sys.argv[1:])
with zipfile.ZipFile(destination, 'w', compression=zipfile.ZIP_DEFLATED) as archive:
    for path in sorted(list((game / 'Data/Core').rglob('*')) + list((game / 'Rendering/Shaders').rglob('*'))):
        if path.is_file():
            if path.is_symlink() or path.suffix.lower() == '.dll':
                raise ValueError(f'Core-only staging rejects {path}')
            archive.write(path, path.relative_to(game))
