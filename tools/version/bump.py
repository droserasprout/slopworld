"""Prepare product versions for a human-maintained, dated changelog release."""

import argparse
from pathlib import Path

from tools import ROOT
from tools.release.changelog import description
from tools.utils import log
from tools.utils import run_main
from tools.version import product


def bump(version: str, repository: Path = ROOT) -> list[Path]:
    planned = product.changes(version, repository)
    description(repository / 'CHANGELOG.md', version)
    written: list[Path] = []
    try:
        for path, (_, updated) in planned.items():
            # Include the current write in rollback in case it fails after truncation.
            written.append(path)
            path.write_text(updated)
    except OSError:
        for path in reversed(written):
            path.write_text(planned[path][0])
        raise
    return written


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('version')
    args = parser.parse_args()
    paths = bump(args.version)
    for path in paths:
        log(f'Updated {path.relative_to(ROOT)}')
    if not paths:
        log(f'Product versions already match {args.version}.')


if __name__ == '__main__':
    run_main(main)
