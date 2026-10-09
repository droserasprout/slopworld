"""Expand shared release metadata in mdBook without changing human-authored source files."""

import json
import re
import sys
from pathlib import Path
from typing import Any

from tools import ROOT
from tools.utils import run_main
from tools.version.metadata import TAG_FORMAT
from tools.version.metadata import TAG_PREFIX
from tools.version.metadata import package_version

PLACEHOLDER = re.compile(r'\{\{#constant\s+([^{}]*?)\s*\}\}')


def values(repository: Path = ROOT) -> dict[str, str]:
    version = package_version(repository)
    return {
        'release_version': version,
        'release_tag': TAG_PREFIX + version,
        'release_tag_format': TAG_FORMAT,
    }


def expand(content: str, constants: dict[str, str]) -> str:
    def replace(match: re.Match[str]) -> str:
        name = match[1]
        if name not in constants:
            raise ValueError(f'Unknown documentation constant: {name}')
        return constants[name]

    return PLACEHOLDER.sub(replace, content)


def render(book: dict[str, Any], constants: dict[str, str]) -> None:
    def visit(item: Any) -> None:
        if isinstance(item, dict):
            for key, value in item.items():
                if key == 'content' and isinstance(value, str):
                    item[key] = expand(value, constants)
                else:
                    visit(value)
        elif isinstance(item, list):
            for child in item:
                visit(child)

    visit(book)


def main() -> None:
    if sys.argv[1:2] == ['supports']:
        return
    context, book = json.load(sys.stdin)
    render(book, values(Path(context['root']).parent))
    json.dump(book, sys.stdout)


if __name__ == '__main__':
    run_main(main)
