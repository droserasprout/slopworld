"""Protect the release bundle, attestation boundary, and non-publishing previews."""

import hashlib
import json
import subprocess
from pathlib import Path
from unittest.mock import patch

import pytest
import yaml

from tools import ROOT
from tools.release import changelog
from tools.release import ci
from tools.release import gates
from tools.release import publish


@pytest.fixture
def release() -> gates.Release:
    return gates.Release('a' * 40, 'v1.2.3', '1.2.3')


@pytest.fixture
def bundle(tmp_path: Path, release: gates.Release, monkeypatch: pytest.MonkeyPatch) -> Path:
    names = ci.asset_names(release.version)
    for name in names:
        (tmp_path / name).write_text(name)
    sums = ''.join(f'{ci.digest(tmp_path / name)}  {name}\n' for name in names[:-2])
    (tmp_path / 'SHA256SUMS').write_text(sums)
    monkeypatch.setattr(changelog, 'description', lambda *args: 'release-notes.md')
    (tmp_path / 'release.json').write_text(json.dumps(ci.manifest(tmp_path, release)))
    return tmp_path


def test_bundle_verifies_every_file_and_identity(bundle: Path, release: gates.Release) -> None:
    assert len(ci.verify(bundle, release)) == 8
    with pytest.raises(ValueError, match='manifest'):
        ci.verify(bundle, gates.Release('b' * 40, release.tag, release.version))


@pytest.mark.parametrize('damage', ['changed', 'missing', 'extra', 'symlink', 'empty', 'traversal'])
def test_damaged_bundle_is_rejected(bundle: Path, release: gates.Release, damage: str) -> None:
    payload = bundle / ci.asset_names(release.version)[0]
    if damage == 'changed':
        payload.write_text('changed')
    elif damage == 'missing':
        payload.unlink()
    elif damage == 'extra':
        (bundle / 'unreviewed.zip').write_text('extra')
    elif damage == 'symlink':
        payload.unlink()
        payload.symlink_to('release-notes.md')
    elif damage == 'empty':
        payload.write_bytes(b'')
    else:
        document = json.loads((bundle / 'release.json').read_text())
        document['files']['../outside'] = hashlib.sha256(b'outside').hexdigest()
        (bundle / 'release.json').write_text(json.dumps(document))
    with pytest.raises(ValueError):
        ci.verify(bundle, release)


def test_checksums_cannot_disagree_with_attested_manifest(bundle: Path, release: gates.Release) -> None:
    (bundle / 'SHA256SUMS').write_text('incorrect\n')
    (bundle / 'release.json').write_text(json.dumps(ci.manifest(bundle, release)))
    with pytest.raises(ValueError, match='SHA256SUMS'):
        ci.verify(bundle, release)


def test_preview_cannot_publish(bundle: Path, release: gates.Release) -> None:
    with patch.object(ci, 'run') as run, patch.object(publish, 'publish') as upload:
        with pytest.raises(ValueError, match='Preview'):
            ci.upload(bundle, gates.Release(release.revision, '', release.version))
        run.assert_not_called()
        upload.assert_not_called()


@pytest.mark.parametrize('failure', [None, 0, 7])
def test_every_attestation_passes_before_publication_without_rebuilding(
    bundle: Path, release: gates.Release, monkeypatch: pytest.MonkeyPatch, failure: int | None
) -> None:
    monkeypatch.setenv('GITHUB_REPOSITORY', 'owner/repo')
    monkeypatch.setenv('GH', 'gh')
    results: list[subprocess.CalledProcessError | None] = [None] * 8
    if failure is not None:
        results[failure] = subprocess.CalledProcessError(1, ['gh'])
    with (
        patch.object(ci, 'run', side_effect=results) as run,
        patch.object(ci, 'validate') as validate,
        patch.object(publish, 'publish') as upload,
        patch.object(ci, 'build') as build,
    ):
        if failure is not None:
            with pytest.raises(subprocess.CalledProcessError):
                ci.upload(bundle, release)
            upload.assert_not_called()
        else:
            ci.upload(bundle, release)
            assert run.call_count == 8
            validate.assert_called_once_with(release)
            assert upload.call_args.kwargs == {'require_tag': True}
        build.assert_not_called()
        for call in run.call_args_list:
            args = call.args[0]
            assert args[:3] == ['gh', 'attestation', 'verify']
            assert args[args.index('--source-digest') + 1] == release.revision
            assert args[args.index('--source-ref') + 1] == 'refs/tags/v1.2.3'
            assert args[args.index('--signer-workflow') + 1] == 'owner/repo/.github/workflows/release.yml'
            assert '--deny-self-hosted-runners' in args


def test_deleted_remote_tag_is_not_recreated(bundle: Path, release: gates.Release) -> None:
    with patch.object(publish, 'github_json', return_value=None), patch.object(publish, 'run') as run:
        with pytest.raises(ValueError, match='missing'):
            publish.publish(['gh'], 'owner/repo', bundle, release.revision, release.tag, [], require_tag=True)
        run.assert_not_called()


def test_ci_does_not_reuse_old_output(bundle: Path, release: gates.Release) -> None:
    with patch.object(ci, 'run') as run:
        with pytest.raises(ValueError, match='empty'):
            ci.build(bundle, release)
        run.assert_not_called()


def test_failed_build_cannot_package(tmp_path: Path, release: gates.Release, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv('JUST_CMD', 'just')
    with (
        patch.object(ci, 'validate'),
        patch.object(ci, 'run', side_effect=subprocess.CalledProcessError(1, ['just'])),
        patch.object(publish, 'package') as package,
    ):
        with pytest.raises(subprocess.CalledProcessError):
            ci.build(tmp_path, release)
        package.assert_not_called()


def test_release_workflow_gates_publication_and_downloads_exact_artifact() -> None:
    workflow = yaml.load((ROOT / '.github/workflows/release.yml').read_text(), Loader=yaml.BaseLoader)
    jobs = workflow['jobs']
    assert jobs['test']['with']['ref'] == '${{ needs.prepare.outputs.commit }}'
    assert set(jobs['package']['needs']) == {'prepare', 'test'}
    assert set(jobs['publish']['needs']) == {'prepare', 'package', 'image'}
    for name in ('publish', 'image'):
        assert jobs[name]['if'] == "needs.prepare.outputs.mode == 'release'"
    steps = jobs['publish']['steps']
    download = next(step for step in steps if step.get('uses', '').startswith('actions/download-artifact@'))
    assert download['with']['artifact-ids'] == '${{ needs.package.outputs.artifact }}'
    assert steps[-1]['run'] == 'just release-ci-publish'
    assert jobs['package']['permissions']['contents'] == 'read'
    assert jobs['publish']['permissions']['contents'] == 'write'
    assert workflow['concurrency']['cancel-in-progress'] == 'false'


@pytest.mark.parametrize('state', ['published', 'missing', 'moved', 'matching'])
def test_preflight_blocks_existing_release_or_changed_remote_tag(
    release: gates.Release, monkeypatch: pytest.MonkeyPatch, state: str
) -> None:
    monkeypatch.setenv('GITHUB_REPOSITORY', 'owner/repo')
    remote = None if state == 'missing' else {'sha': release.revision if state == 'matching' else 'other'}
    responses = [{'id': 1} if state == 'published' else None, remote]
    with patch.object(publish, 'github_json', side_effect=responses):
        if state == 'matching':
            ci.preflight(release)
        else:
            with pytest.raises(ValueError):
                ci.preflight(release)


@pytest.mark.parametrize('setting', ['RELEASE_REVISION', 'RELEASE_VERSION'])
def test_preview_rejects_changed_coordinator_identity(monkeypatch: pytest.MonkeyPatch, setting: str) -> None:
    monkeypatch.delenv('RELEASE_REVISION', raising=False)
    monkeypatch.delenv('RELEASE_VERSION', raising=False)
    monkeypatch.setenv(setting, 'different')
    with (
        patch.object(gates, 'git', side_effect=['', 'a' * 40]),
        patch.object(ci, 'package_version', return_value='1.2.3'),
        patch('tools.version.product.check'),
        pytest.raises(ValueError, match=setting),
    ):
        ci.identity(preview=True)
