#!/usr/bin/env python3
"""Stage the canonical project and third-party notices into the mod distribution."""

import argparse
import shutil

from tools import ROOT


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true', help='verify staged copies')
    args = parser.parse_args()
    source = ROOT / 'licenses'
    destination = ROOT / 'mod/About/ThirdPartyNotices'
    project_license = ROOT / 'mod/About/LICENSE'
    if args.check:
        expected = {p.relative_to(source): p.read_bytes() for p in source.rglob('*') if p.is_file()}
        actual = {p.relative_to(destination): p.read_bytes() for p in destination.rglob('*') if p.is_file()}
        if (
            expected != actual
            or not project_license.exists()
            or (project_license.read_bytes() != (ROOT / 'LICENSE').read_bytes())
        ):
            parser.exit(1, 'staged licenses differ; run just stage-licenses\n')
        return
    # This directory is generated in full, so removed upstream notices cannot linger.
    if destination.exists():
        shutil.rmtree(destination)
    shutil.copytree(source, destination)
    shutil.copyfile(ROOT / 'LICENSE', project_license)


if __name__ == '__main__':
    main()
