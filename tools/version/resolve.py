"""Resolve release tags or UTC date/commit versions, matching slopd/src/version.rs."""

import argparse
import datetime as dt
import re
from pathlib import Path

from tools import ROOT
from tools.utils import run


def resolve(fallback: str, tag: str | None, commit: str | None, date: str) -> str:
    if tag and re.fullmatch(r'v?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)', tag):
        return tag.removeprefix('v')
    if commit and commit.strip():
        return f'{fallback}-{date}-{commit}'
    return fallback


def from_git(fallback: str, repository: Path = ROOT) -> str:
    def git(*args: str) -> str | None:
        try:
            result = run(
                ['git', '-C', str(repository), *args], capture_output=True, text=True, errors='replace', check=False
            )
        except OSError:
            return None
        return result.stdout.strip() if result.returncode == 0 else None

    return resolve(
        fallback,
        git('describe', '--tags', '--exact-match', 'HEAD'),
        git('rev-parse', '--short', 'HEAD'),
        dt.datetime.now(dt.timezone.utc).strftime('%Y%m%d'),
    )


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('fallback')
    args = parser.parse_args()
    print(from_git(args.fallback))


if __name__ == '__main__':
    main()
