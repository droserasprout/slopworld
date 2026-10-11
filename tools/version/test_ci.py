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
    prepare = workflow['jobs']['prepare']
    steps = prepare['steps']
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
    assert 'image=ghcr.io/testowner/slopcar\n' in metadata
    assert prepare['outputs'] == {
        'image': '${{ steps.image.outputs.image }}',
        'version': '${{ steps.image.outputs.version }}',
    }
    publish = workflow['jobs']['publish']
    assert publish['needs'] == 'prepare'
    assert publish['uses'] == 'docker/github-builder/.github/workflows/build.yml@v1'
    build = publish['with']
    assert build['meta-images'] == '${{ needs.prepare.outputs.image }}'
    assert build['meta-tags'].splitlines() == [
        'type=raw,value=sha-${{ github.sha }}',
        'type=raw,value=${{ needs.prepare.outputs.version }}',
    ]
    assert build['meta-flavor'] == 'latest=false'
    assert 'SLOPWORLD_BUILD_VERSION=${{ needs.prepare.outputs.version }}' in build['build-args']
    assert 'org.opencontainers.image.version=${{ needs.prepare.outputs.version }}' in build['labels']


def test_sidecar_builds_only_trigger_on_release_tag_pushes() -> None:
    # BaseLoader preserves the YAML key "on" instead of treating it as a boolean.
    workflow = yaml.load((ROOT / '.github/workflows/image.yml').read_text(), Loader=yaml.BaseLoader)
    assert set(workflow['on']) == {'push'}
    assert set(workflow['on']['push']) == {'tags'}
    assert workflow['on']['push']['tags'] == [TAG_GLOB]


def test_test_workflow_uses_the_same_release_tag_filter() -> None:
    workflow = yaml.load((ROOT / '.github/workflows/test.yml').read_text(), Loader=yaml.BaseLoader)
    assert workflow['on']['push']['tags'] == [TAG_GLOB]
