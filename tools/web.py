"""Own checkout build-output cleanup; installed runtimes and compiler caches are separate."""

import argparse
import shutil
from pathlib import Path

from tools import ROOT
from tools.utils import run_main


def clean(root: Path = ROOT) -> None:
    component = root / 'slopweb'
    # Project directories are authored inputs; never walk generated/private caches.
    projects = [component / 'runtime', component / 'offline']
    for owner in (component / 'runtime', component / 'tests/runtime'):
        projects.extend(
            path.parent
            for path in owner.glob('*/*.csproj')
            if path.parent.name not in ('out', 'bin', 'obj', '.local') and not path.parent.is_symlink()
        )
    directories = [component / 'out', component / 'host/target']
    directories.extend(project / name for project in projects for name in ('bin', 'obj'))
    for path in directories:
        if path.is_symlink():
            path.unlink()
        elif path.exists():
            shutil.rmtree(path)
    for name in ('local-fonts.mjs', 'local-fonts.wasm', 'local-fonts.LICENSE.txt'):
        (component / 'runtime/wwwroot' / name).unlink(missing_ok=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=('clean',))
    parser.parse_args()
    clean()


if __name__ == '__main__':
    run_main(main)
