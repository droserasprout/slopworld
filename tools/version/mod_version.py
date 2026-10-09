"""Resolve mod metadata using the same fallback and tag rules as Rust builds."""

from tools.version.metadata import package_version
from tools.version.resolve import from_git


def main() -> None:
    print(from_git(package_version()))


if __name__ == '__main__':
    main()
