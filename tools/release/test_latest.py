"""Protect release archive contents and failure ordering without game or GitHub access."""

import hashlib
import subprocess
import tarfile
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path
from unittest.mock import patch

import pytest

from tools.release import latest


def test_runtime_allowlist_matches_mod_project_references():
    project = ET.parse(latest.ROOT / 'mod/Source/SlopWorld/SlopWorld.csproj')
    runtime = {
        reference.attrib['Include']
        for reference in project.findall('.//Reference')
        if (reference.findtext('HintPath') or '').startswith('../../Assemblies/')
    }
    assert set(latest.MOD_ASSEMBLIES) == runtime | {'SlopWorld'}


@pytest.fixture
def inputs(tmp_path, monkeypatch):
    root = tmp_path / 'checkout'
    root.mkdir()
    monkeypatch.setattr(latest, 'ROOT', root)

    def write(name, content=b'release input'):
        path = root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content)
        return path

    for binary in ('slopd', 'slopctl', 'slopworld'):
        write(f'slopd/target/release/{binary}').chmod(0o755)
    for name in (
        'slopd/slopd.service',
        'packaging/slopworld.desktop',
        'LICENSE',
        'licenses/dependency.txt',
        'mod/About/LICENSE',
        'mod/About/ThirdPartyNotices/NOTICE.txt',
    ):
        write(name)
    for assembly in latest.MOD_ASSEMBLIES:
        write(f'mod/Assemblies/{assembly}.dll')
    tracked = [
        'mod/About/About.xml',
        'mod/Defs/Test.xml',
        'mod/Themes/example.toml',
        'mod/Source/SlopWorld/Test.cs',
        'mod/Assemblies/Assembly-CSharp.dll',
    ]
    for name in tracked:
        write(name)
    write('mod/Textures/untracked.png')
    monkeypatch.setattr(latest, 'git', lambda *args: '\0'.join(tracked) + '\0')
    return root


def test_archives_include_runtime_licenses_metadata_and_exclude_game_and_local_files(inputs, tmp_path):
    output = tmp_path / 'release output'
    assets = latest.package(output, 'a' * 40, '0.1.0-snapshot')
    with tarfile.open(assets[0]) as archive:
        prefix = latest.DAEMON_NAME
        assert archive.getmember(f'{prefix}/bin/slopd').mode & 0o111
        assert archive.extractfile(f'{prefix}/REVISION').read() == b'a' * 40 + b'\n'
        assert f'{prefix}/licenses/dependency.txt' in archive.getnames()
    with zipfile.ZipFile(assets[1]) as archive:
        names = archive.namelist()
        assert 'SlopWorld/About/LICENSE' in names
        assert 'SlopWorld/About/ThirdPartyNotices/NOTICE.txt' in names
        assert 'SlopWorld/Themes/example.toml' in names
        assert archive.read('SlopWorld/VERSION') == b'0.1.0-snapshot\n'
        assert {Path(name).stem for name in names if name.endswith('.dll')} == set(latest.MOD_ASSEMBLIES)
        assert not any('Source/' in name or 'untracked' in name for name in names)
    for line in assets[2].read_text().splitlines():
        digest, name = line.split('  ')
        assert digest == hashlib.sha256((output / name).read_bytes()).hexdigest()


def test_missing_runtime_input_preserves_previous_archives(inputs, tmp_path):
    output = tmp_path / 'dist'
    assets = latest.package(output, 'a' * 40, 'snapshot')
    original = [asset.read_bytes() for asset in assets]
    (inputs / 'mod/Assemblies/Tomlyn.dll').unlink()
    with pytest.raises(ValueError, match='Tomlyn'):
        latest.package(output, 'b' * 40, 'snapshot-2')
    assert [asset.read_bytes() for asset in assets] == original


def test_dirty_or_changed_checkout_is_rejected():
    with patch.object(latest, 'git', return_value=' M tracked'):
        with pytest.raises(ValueError, match='clean checkout'):
            latest.validate_checkout('revision')
    with patch.object(latest, 'git', side_effect=['', 'other revision']):
        with pytest.raises(ValueError, match='HEAD changed'):
            latest.validate_checkout('revision')


def test_build_forces_release_and_one_version_and_stops_on_failure(monkeypatch):
    monkeypatch.setenv('JUST_CMD', '/path with spaces/just')
    monkeypatch.setenv('VERSION', 'explicit-version')
    with patch.object(latest, 'run') as run, patch.object(latest, 'validate_checkout') as validate:
        assert latest.build('revision') == 'explicit-version'
        assert run.call_args.args[0] == [
            '/path with spaces/just',
            'BUILD=release',
            'VERSION=explicit-version',
            'all',
            'check-licenses',
        ]
        validate.assert_called_once_with('revision')
    with patch.object(latest, 'run', side_effect=subprocess.CalledProcessError(1, ['just'])):
        with pytest.raises(subprocess.CalledProcessError):
            latest.build('revision')


@pytest.mark.parametrize('existing', [False, True])
def test_publish_creates_or_updates_tag_before_upload_and_edits_after_upload(tmp_path, existing):
    assets = [tmp_path / name for name in latest.ASSET_NAMES]
    with (
        patch.object(
            latest,
            'github_json',
            side_effect=[{'id': 1} if existing else None, {'ref': 'latest'} if existing else None],
        ),
        patch.object(latest, 'run') as run,
    ):
        latest.publish(['gh'], 'owner/repo', tmp_path, 'revision', assets)
    calls = [call.args[0] for call in run.call_args_list]
    assert calls[0][:2] == ['gh', 'api']
    assert 'PATCH' in calls[0] if existing else 'POST' in calls[0]
    assert 'sha=revision' in calls[0]
    if existing:
        assert calls[1][:4] == ['gh', 'release', 'upload', 'latest']
        assert '--clobber' in calls[1]
        assert calls[2][:4] == ['gh', 'release', 'edit', 'latest']
    else:
        assert calls[1][:4] == ['gh', 'release', 'create', 'latest']
        assert '--verify-tag' in calls[1]
    assert '--latest' in calls[-1]


def test_immutable_release_and_upload_failure_stop_publication(tmp_path):
    with patch.object(latest, 'github_json', return_value={'immutable': True}), patch.object(latest, 'run') as run:
        with pytest.raises(ValueError, match='immutable'):
            latest.publish(['gh'], 'owner/repo', tmp_path, 'revision', [])
        run.assert_not_called()
    with (
        patch.object(latest, 'github_json', return_value={'id': 1}),
        patch.object(latest, 'run', side_effect=[None, subprocess.CalledProcessError(1, ['gh'])]) as run,
    ):
        with pytest.raises(subprocess.CalledProcessError):
            latest.publish(['gh'], 'owner/repo', tmp_path, 'revision', [])
        assert run.call_count == 2


@pytest.mark.parametrize(
    'status, stderr', [(404, 'gh: Not Found (HTTP 404)'), (403, 'gh: Forbidden (HTTP 403)'), (1, 'connection refused')]
)
def test_only_http_404_is_treated_as_missing(status, stderr):
    result = subprocess.CompletedProcess(['gh'], status, '', stderr)
    with patch.object(latest, 'run', return_value=result):
        if status == 404:
            assert latest.github_json(['gh'], 'endpoint') is None
        else:
            with pytest.raises(subprocess.CalledProcessError):
                latest.github_json(['gh'], 'endpoint')


def test_failed_build_never_packages_or_publishes(monkeypatch):
    monkeypatch.setattr('sys.argv', ['latest', 'publish'])
    with (
        patch.object(latest.platform, 'system', return_value='Linux'),
        patch.object(latest.platform, 'machine', return_value='x86_64'),
        patch.object(latest, 'git', return_value='revision'),
        patch.object(latest, 'validate_checkout'),
        patch.object(latest, 'run', return_value=subprocess.CompletedProcess([], 0, 'owner/repo\n')),
        patch.object(latest, 'build', side_effect=subprocess.CalledProcessError(1, ['just'])),
        patch.object(latest, 'package') as package,
        patch.object(latest, 'publish') as publish,
    ):
        with pytest.raises(subprocess.CalledProcessError):
            latest.main()
        package.assert_not_called()
        publish.assert_not_called()
