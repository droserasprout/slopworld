"""Exercise image CI version selection without building or publishing containers."""

import os
from pathlib import Path

import pytest
import yaml

from tools import ROOT
from tools.utils import run
from tools.version.metadata import TAG_GLOB


@pytest.mark.parametrize(
    'ref, event, release_tag, expected',
    [
        ('refs/heads/main', 'push', '', None),
        ('refs/tags/v0.0.1', 'push', '', '0.0.1'),
        ('refs/tags/1.2.3', 'push', '', None),
        ('refs/tags/v1.2.3', 'push', '', '1.2.3'),
        ('refs/tags/v01.2.3', 'push', '', None),
        ('refs/tags/vnext', 'push', '', None),
        ('refs/heads/main', 'workflow_dispatch', 'v0.0.5', '0.0.5'),
        ('refs/heads/main', 'workflow_dispatch', 'v999.0.0', None),
        ('refs/heads/main', 'workflow_dispatch', 'invalid', None),
        ('refs/heads/other', 'workflow_dispatch', 'v0.0.5', None),
    ],
)
def test_image_tags_and_embedded_version_agree(
    tmp_path: Path, ref: str, event: str, release_tag: str, expected: str | None
) -> None:
    workflow = yaml.safe_load((ROOT / '.github/workflows/image.yml').read_text())
    prepare = workflow['jobs']['prepare']
    steps = prepare['steps']
    script = next(step['run'] for step in steps if step.get('id') == 'image')
    output = tmp_path / 'output'
    run(['git', 'init', '--quiet', str(tmp_path)])
    run(
        [
            'git',
            '-c',
            'user.name=Test',
            '-c',
            'user.email=test@example.com',
            'commit',
            '--quiet',
            '--allow-empty',
            '-m',
            'release',
        ],
        cwd=tmp_path,
    )
    run(['git', 'tag', 'v0.0.5'], cwd=tmp_path)
    environment = os.environ | {
        'PYTHONPATH': str(ROOT),
        'OWNER': 'TestOwner',
        'GITHUB_REF': ref,
        'GITHUB_EVENT_NAME': event,
        'RELEASE_TAG': release_tag,
        'GITHUB_REF_NAME': ref.rsplit('/', 1)[-1],
        'GITHUB_SHA': 'a' * 40,
        'GITHUB_OUTPUT': str(output),
    }
    result = run(['bash', '-euc', script], cwd=tmp_path, env=environment, check=False)
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


def test_sidecar_builds_trigger_on_release_tags_or_explicit_dispatch() -> None:
    # BaseLoader preserves the YAML key "on" instead of treating it as a boolean.
    workflow = yaml.load((ROOT / '.github/workflows/image.yml').read_text(), Loader=yaml.BaseLoader)
    assert set(workflow['on']) == {'push', 'workflow_dispatch'}
    assert set(workflow['on']['push']) == {'tags'}
    assert workflow['on']['push']['tags'] == [TAG_GLOB]
    assert workflow['on']['workflow_dispatch']['inputs']['release_tag']['required'] == 'true'


def test_test_workflow_uses_the_same_release_tag_filter() -> None:
    workflow = yaml.load((ROOT / '.github/workflows/test.yml').read_text(), Loader=yaml.BaseLoader)
    assert workflow['on']['push']['tags'] == [TAG_GLOB]
