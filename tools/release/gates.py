"""Own release checkout eligibility and validation stages; publish owns artifacts and transport."""

import os
from dataclasses import dataclass

from tools import ROOT
from tools.utils import run
from tools.version import product
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
    branch = git('rev-parse', '--abbrev-ref', 'HEAD')
    if branch not in ('main', 'HEAD'):
        raise ValueError('Release requires main or a detached release commit on main.')
    revision = git('rev-parse', 'HEAD')
    # Hosted runners check out an immutable SHA. Require it to belong to fetched
    # main, rather than trusting a tag on an unrelated branch.
    main = os.environ.get('RELEASE_MAIN_REF', 'refs/heads/main')
    if git('merge-base', revision, main) != revision:
        raise ValueError('Release commit must belong to main.')
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
    product.check(version, ROOT)
    return Release(revision, tag, version)


def validate(release: Release) -> None:
    if prepare() != release:
        raise ValueError('Release commit or tag changed during validation; rerun the recipe.')


def build(release: Release) -> None:
    # Generation can modify sources. Recheck identity and cleanliness after every
    # stage so changed inputs cannot enter a tagged release.
    command = [os.environ['JUST_CMD'], 'BUILD=release', f'VERSION={release.version}']
    validate(release)
    for recipes in (
        ('generate-all',),
        ('lint', 'mod-check-build'),
        ('test',),
        ('generate-check-protocol', 'generate-check-licenses', 'docs-build'),
        ('build',),
    ):
        run([*command, 'MANAGED=rimref', *recipes])
        validate(release)
