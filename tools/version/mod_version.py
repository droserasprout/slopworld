"""Resolve mod metadata using the same fallback and tag rules as Rust builds."""

import subprocess
import tomllib
from pathlib import Path

from tools import ROOT


def main() -> None:
    with (ROOT / 'slopd/Cargo.toml').open('rb') as manifest:
        fallback = tomllib.load(manifest)['package']['version']
    subprocess.run([str(Path(__file__).with_name('version.sh')), fallback], check=True)


if __name__ == '__main__':
    main()
