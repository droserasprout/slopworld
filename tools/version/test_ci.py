"""Exercise image CI version selection without building or publishing containers."""

import os
from pathlib import Path

import pytest
import yaml

from tools import ROOT
from tools.utils import run


@pytest.mark.parametrize(
    'ref, expected',
    [
        ('refs/heads/main', '0.0.1-20261009-abcdef'),
        ('refs/tags/v0.0.1', '0.0.1'),
        ('refs/tags/1.2.3', '1.2.3'),
        ('refs/tags/v01.2.3', None),
        ('refs/tags/vnext', None),
    ],
)
def test_image_tags_and_embedded_version_agree(tmp_path: Path, ref: str, expected: str | None) -> None:
    workflow = yaml.safe_load((ROOT / '.github/workflows/image.yml').read_text())
    steps = workflow['jobs']['publish']['steps']
    script = next(step['run'] for step in steps if step.get('id') == 'image')
    # Stand in for the independently tested Git resolver; the workflow must use
    # the triggering release tag even when another tag exists at the same commit.
    resolver = tmp_path / 'python3'
    resolver.write_text('#!/bin/sh\necho 0.0.1-20261009-abcdef\n')
    resolver.chmod(0o755)
    output = tmp_path / 'output'
    environment = os.environ | {
        'PATH': f'{tmp_path}:{os.environ["PATH"]}',
        'OWNER': 'TestOwner',
        'GITHUB_REF': ref,
        'GITHUB_REF_NAME': ref.rsplit('/', 1)[-1],
        'GITHUB_SHA': 'a' * 40,
        'GITHUB_OUTPUT': str(output),
    }
    result = run(['bash', '-euc', script], env=environment, check=False)
    if expected is None:
        assert result.returncode != 0
        assert not output.exists()
        return
    assert result.returncode == 0
    metadata = output.read_text()
    assert f'version={expected}\n' in metadata
    assert f'ghcr.io/testowner/slopcar:{expected}\n' in metadata
    assert f'ghcr.io/testowner/slopcar:sha-{"a" * 40}\n' in metadata
    assert ':latest' not in metadata
    build = next(step['with'] for step in steps if step.get('name') == 'Build and publish sidecar')
    assert 'SLOPWORLD_BUILD_VERSION=${{ steps.image.outputs.version }}' in build['build-args']
    assert 'org.opencontainers.image.version=${{ steps.image.outputs.version }}' in build['labels']
