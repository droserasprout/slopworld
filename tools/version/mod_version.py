"""Resolve mod metadata using the same fallback and tag rules as Rust builds."""

import tomllib

from tools import ROOT
from tools.version.resolve import from_git


def main() -> None:
    with (ROOT / 'slopd/Cargo.toml').open('rb') as manifest:
        fallback = tomllib.load(manifest)['package']['version']
    print(from_git(fallback))


if __name__ == '__main__':
    main()
