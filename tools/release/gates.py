"""Own release checkout eligibility and validation stages; publish owns artifacts and transport."""

import os
from dataclasses import dataclass

from tools.utils import run
from tools.version.metadata import TAG_FORMAT
from tools.version.metadata import release_version


@dataclass(frozen=True)
class Release:
    revision: str
    tag: str
    version: str


def git(*arguments: str) -> str:
    return run(['git', *arguments], capture_output=True, text=True).stdout.strip()


def prepare() -> Release:
    if git('status', '--porcelain', '--untracked-files=normal'):
        raise ValueError('Release requires a clean checkout; review and commit changes first.')
    if git('rev-parse', '--abbrev-ref', 'HEAD') != 'main':
        raise ValueError('Release requires the main branch; detached HEAD is not allowed.')
    revision = git('rev-parse', 'HEAD')
    tags = {
        tag: version
        for tag in git('tag', '--points-at', 'HEAD').splitlines()
        if (version := release_version(tag)) is not None
    }
    if len(tags) != 1:
        raise ValueError(f'HEAD must have one unambiguous release tag ({TAG_FORMAT}).')
    tag, version = next(iter(tags.items()))
    override = os.environ.get('VERSION')
    if override and override != version:
        raise ValueError('VERSION must match the release tag at HEAD.')
    return Release(revision, tag, version)


def validate(release: Release) -> None:
    if prepare() != release:
        raise ValueError('Release commit or tag changed during validation; rerun the recipe.')


def build(release: Release) -> None:
    # Refresh and Python lint can modify sources. Check after each stage so no
    # generated or formatted changes silently enter a tagged release.
    command = [os.environ['JUST_CMD'], 'BUILD=release', f'VERSION={release.version}']
    validate(release)
    for recipes in (
        ('refresh',),
        ('lint',),
        ('test', 'check-generated', 'check-licenses', 'test-text-sprites', 'docs'),
        ('all',),
    ):
        run([*command, *recipes])
        validate(release)
