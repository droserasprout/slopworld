"""Replace prototype code while retaining explicitly prepared presentation assets."""

import shutil
import sys
import tempfile
from pathlib import Path


def refresh(source: Path, destination: Path) -> None:
    # Production preparation creates a fresh immutable runtime in slopweb/launch.py.
    # These snapshots belong only to the developer's retained prototype server.
    with tempfile.TemporaryDirectory(prefix='.refresh-', dir=destination.parent) as temporary:
        staged = Path(temporary) / 'runtime'
        shutil.copytree(source, staged)
        for name in ('fonts', 'background', 'portraits', 'reference-appearance.json'):
            previous = destination / 'assets' / name
            target = staged / 'assets' / name
            if not previous.exists():
                continue
            target.parent.mkdir(parents=True, exist_ok=True)
            if target.is_dir():
                shutil.rmtree(target)
            elif target.exists():
                target.unlink()
            if previous.is_dir():
                shutil.copytree(previous, target)
            else:
                shutil.copy2(previous, target)
        backup = Path(temporary) / 'previous'
        if destination.exists():
            destination.rename(backup)
        try:
            staged.rename(destination)
        except BaseException:
            if backup.exists():
                backup.rename(destination)
            raise


if __name__ == '__main__':
    refresh(Path(sys.argv[1]), Path(sys.argv[2]))
