"""Stage source installs and Arch package mods, including snapshots without Git metadata.

Release staging owns the asset directory and runtime DLL allowlists. This owner
copies source assets and canonical notices without relying on local license refreshes.
"""

import argparse
import shutil
from pathlib import Path

from tools import ROOT
from tools.release import staging
from tools.utils import run_main


def stage(mod: Path) -> None:
    mod.mkdir(parents=True, exist_ok=True)
    for directory in staging.MOD_DIRECTORIES:
        # About's generated notices may be absent or stale in a source snapshot.
        # Copy only its source content, then install canonical notices below.
        ignore = shutil.ignore_patterns('LICENSE', 'ThirdPartyNotices') if directory == 'About' else None
        shutil.copytree(ROOT / 'mod' / directory, mod / directory, ignore=ignore)
    for assembly in staging.MOD_ASSEMBLIES:
        staging.copy_file(ROOT / 'mod/Assemblies' / f'{assembly}.dll', mod / 'Assemblies' / f'{assembly}.dll')
    staging.copy_file(ROOT / 'LICENSE', mod / 'About/LICENSE')
    shutil.copytree(ROOT / 'licenses', mod / 'About/ThirdPartyNotices')


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('destination', type=Path)
    args = parser.parse_args()
    stage(args.destination)


if __name__ == '__main__':
    run_main(main)
