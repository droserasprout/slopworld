"""Exercise image CI version selection without building or publishing containers."""

import os
from pathlib import Path

import pytest
import yaml

from tools import ROOT
from tools.utils import run
from tools.version.metadata import TAG_GLOB


@pytest.mark.parametrize(
    'ref, expected',
    [
        ('refs/heads/main', None),
        ('refs/tags/v0.0.1', '0.0.1'),
        ('refs/tags/1.2.3', None),
        ('refs/tags/v1.2.3', '1.2.3'),
        ('refs/tags/v01.2.3', None),
        ('refs/tags/vnext', None),
    ],
)
def test_image_tags_and_embedded_version_agree(tmp_path: Path, ref: str, expected: str | None) -> None:
    workflow = yaml.safe_load((ROOT / '.github/workflows/image.yml').read_text())
    steps = workflow['jobs']['publish']['steps']
    script = next(step['run'] for step in steps if step.get('id') == 'image')
    output = tmp_path / 'output'
    environment = os.environ | {
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


def test_sidecar_builds_only_trigger_on_release_tag_pushes() -> None:
    # BaseLoader preserves the YAML key "on" instead of treating it as a boolean.
    workflow = yaml.load((ROOT / '.github/workflows/image.yml').read_text(), Loader=yaml.BaseLoader)
    assert set(workflow['on']) == {'push'}
    assert set(workflow['on']['push']) == {'tags'}
    assert workflow['on']['push']['tags'] == [TAG_GLOB]


def test_test_workflow_uses_the_same_release_tag_filter() -> None:
    workflow = yaml.load((ROOT / '.github/workflows/test.yml').read_text(), Loader=yaml.BaseLoader)
    assert workflow['on']['push']['tags'] == [TAG_GLOB]
