"""Protect release archive contents and failure ordering without game or GitHub access."""

import hashlib
import platform
import subprocess
import tarfile
import zipfile
from collections.abc import Callable
from pathlib import Path
from unittest.mock import patch

import pytest

from tools import ROOT
from tools.release import publish
from tools.release import staging


@pytest.fixture
def inputs(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> Path:
    root = tmp_path / 'checkout'
    root.mkdir()
    monkeypatch.setattr(publish, 'ROOT', root)
    monkeypatch.setattr(staging, 'ROOT', root)

    def write(name: str, content: bytes = b'release input') -> Path:
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
    ):
        write(name)
    for assembly in staging.MOD_ASSEMBLIES:
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
    monkeypatch.setattr(
        staging, 'run', lambda *args, **kwargs: subprocess.CompletedProcess([], 0, '\0'.join(tracked) + '\0')
    )
    return root


@pytest.mark.parametrize('staged_notices', [False, True])
def test_archives_include_runtime_licenses_metadata_and_exclude_game_and_local_files(
    inputs: Path, tmp_path: Path, staged_notices: bool
) -> None:
    if staged_notices:
        (inputs / 'mod/About/LICENSE').write_text('stale license')
        notices = inputs / 'mod/About/ThirdPartyNotices'
        notices.mkdir()
        (notices / 'stale.txt').write_text('stale notice')
    output = tmp_path / 'release output'
    assets = publish.package(output, 'a' * 40, '0.1.0-snapshot')
    with tarfile.open(assets[0]) as archive:
        prefix = 'slopworld-0.1.0-snapshot-x86_64-linux'
        assert archive.getmember(f'{prefix}/bin/slopd').mode & 0o111
        revision_file = archive.extractfile(f'{prefix}/REVISION')
        assert revision_file is not None
        assert revision_file.read() == b'a' * 40 + b'\n'
        assert f'{prefix}/licenses/dependency.txt' in archive.getnames()
    with zipfile.ZipFile(assets[1]) as archive:
        names = archive.namelist()
        assert archive.read('SlopWorld/About/LICENSE') == (inputs / 'LICENSE').read_bytes()
        assert (
            archive.read('SlopWorld/About/ThirdPartyNotices/dependency.txt')
            == (inputs / 'licenses/dependency.txt').read_bytes()
        )
        assert 'SlopWorld/About/ThirdPartyNotices/stale.txt' not in names
        assert 'SlopWorld/Themes/example.toml' in names
        assert archive.read('SlopWorld/VERSION') == b'0.1.0-snapshot\n'
        assert {Path(name).stem for name in names if name.endswith('.dll')} == set(staging.MOD_ASSEMBLIES)
        assert not any('Source/' in name or 'untracked' in name for name in names)
    for line in assets[2].read_text().splitlines():
        digest, name = line.split('  ')
        assert digest == hashlib.sha256((output / name).read_bytes()).hexdigest()


def test_missing_runtime_input_preserves_previous_archives(inputs: Path, tmp_path: Path) -> None:
    output = tmp_path / 'dist'
    assets = publish.package(output, 'a' * 40, 'snapshot')
    original = [asset.read_bytes() for asset in assets]
    (inputs / 'mod/Assemblies/Tomlyn.dll').unlink()
    with pytest.raises(ValueError, match='Tomlyn'):
        publish.package(output, 'b' * 40, 'snapshot-2')
    assert [asset.read_bytes() for asset in assets] == original


def test_dirty_or_changed_checkout_is_rejected() -> None:
    with patch.object(publish, 'git', return_value=' M tracked'):
        with pytest.raises(ValueError, match='clean checkout'):
            publish.validate_checkout('revision')
    with patch.object(publish, 'git', side_effect=['', 'other revision']):
        with pytest.raises(ValueError, match='HEAD changed'):
            publish.validate_checkout('revision')


def test_build_forces_release_and_one_version_and_stops_on_failure(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv('JUST_CMD', '/path with spaces/just')
    monkeypatch.setenv('VERSION', 'explicit-version')
    with patch.object(publish, 'run') as run, patch.object(publish, 'validate_checkout') as validate:
        assert publish.build('revision') == 'explicit-version'
        assert run.call_args.args[0] == [
            '/path with spaces/just',
            'BUILD=release',
            'VERSION=explicit-version',
            'all',
        ]
        validate.assert_called_once_with('revision')
    with patch.object(publish, 'run', side_effect=subprocess.CalledProcessError(1, ['just'])):
        with pytest.raises(subprocess.CalledProcessError):
            publish.build('revision')


@pytest.mark.parametrize('existing_tag', [False, True])
def test_publish_preserves_matching_tags_and_creates_missing_tags(tmp_path: Path, existing_tag: bool) -> None:
    with (
        patch.object(publish, 'github_json', side_effect=[None, {'sha': 'revision'} if existing_tag else None]),
        patch.object(publish, 'run') as run,
    ):
        publish.publish(['gh'], 'owner/repo', tmp_path, 'revision', '0.0.1', [])
    calls = [call.args[0] for call in run.call_args_list]
    if not existing_tag:
        assert 'POST' in calls[0]
        assert 'ref=refs/tags/v0.0.1' in calls[0]
        assert 'sha=revision' in calls[0]
    assert len(calls) == (1 if existing_tag else 2)
    assert calls[-1][:4] == ['gh', 'release', 'create', 'v0.0.1']
    assert '--verify-tag' in calls[-1]
    assert not any('PATCH' in call or '--clobber' in call for call in calls)


@pytest.mark.parametrize(
    'responses, message',
    [
        ([{'id': 1}], 'already exists'),
        ([None, {'sha': 'other'}], 'does not point'),
    ],
)
def test_existing_release_or_conflicting_tag_stops_publication(
    tmp_path: Path, responses: list[dict[str, str | int] | None], message: str
) -> None:
    with patch.object(publish, 'github_json', side_effect=responses), patch.object(publish, 'run') as run:
        with pytest.raises(ValueError, match=message):
            publish.publish(['gh'], 'owner/repo', tmp_path, 'revision', '0.0.1', [])
        run.assert_not_called()


def test_tag_creation_failure_stops_publication(tmp_path: Path) -> None:
    with (
        patch.object(publish, 'github_json', return_value=None),
        patch.object(publish, 'run', side_effect=subprocess.CalledProcessError(1, ['gh'])) as run,
    ):
        with pytest.raises(subprocess.CalledProcessError):
            publish.publish(['gh'], 'owner/repo', tmp_path, 'revision', '0.0.1', [])
        assert run.call_count == 1


@pytest.mark.parametrize('version', ['0.0.1', '0.0.1-20261009-abcdef'])
def test_build_uses_shared_version_resolution(monkeypatch: pytest.MonkeyPatch, version: str) -> None:
    monkeypatch.delenv('VERSION', raising=False)
    monkeypatch.setenv('JUST_CMD', 'just')
    with (
        patch.object(publish, 'from_git', return_value=version) as resolve,
        patch.object(publish, 'run') as run,
        patch.object(publish, 'validate_checkout'),
    ):
        assert publish.build('revision') == version
        resolve.assert_called_once_with('0.0.1', ROOT)
        assert f'VERSION={version}' in run.call_args.args[0]


def test_publish_build_rejects_snapshot_before_compilation(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv('VERSION', '0.0.1-20261009-abcdef')
    with patch.object(publish, 'run') as run:
        with pytest.raises(ValueError, match='numeric release tag'):
            publish.build('revision', release=True)
        run.assert_not_called()


def test_publish_build_accepts_numeric_override(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv('VERSION', '0.0.1')
    monkeypatch.setenv('JUST_CMD', 'just')
    with patch.object(publish, 'run') as run, patch.object(publish, 'validate_checkout'):
        assert publish.build('revision', release=True) == '0.0.1'
        assert 'VERSION=0.0.1' in run.call_args.args[0]


@pytest.mark.parametrize(
    'status, stderr', [(404, 'gh: Not Found (HTTP 404)'), (403, 'gh: Forbidden (HTTP 403)'), (1, 'connection refused')]
)
def test_only_http_404_is_treated_as_missing(status: int, stderr: str) -> None:
    result = subprocess.CompletedProcess(['gh'], status, '', stderr)
    with patch.object(publish, 'run', return_value=result):
        if status == 404:
            assert publish.github_json(['gh'], 'endpoint') is None
        else:
            with pytest.raises(subprocess.CalledProcessError):
                publish.github_json(['gh'], 'endpoint')


def test_failed_build_never_packages_or_publishes(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr('sys.argv', ['publish', 'publish'])
    with (
        patch.object(platform, 'system', return_value='Linux'),
        patch.object(platform, 'machine', return_value='x86_64'),
        patch.object(publish, 'git', return_value='revision'),
        patch.object(publish, 'validate_checkout'),
        patch.object(publish, 'run', return_value=subprocess.CompletedProcess([], 0, 'owner/repo\n')),
        patch.object(publish, 'build', side_effect=subprocess.CalledProcessError(1, ['just'])),
        patch.object(publish, 'package') as package,
        patch.object(publish, 'publish') as publish_release,
    ):
        with pytest.raises(subprocess.CalledProcessError):
            publish.main()
        package.assert_not_called()
        publish_release.assert_not_called()


def test_native_packages_are_included_in_checksums_and_outputs(inputs: Path, tmp_path: Path) -> None:
    output = tmp_path / 'release'

    def native(name: str) -> Callable[..., Path]:
        def build(staging: Path, *args: object) -> Path:
            path = staging / name
            path.write_bytes(name.encode())
            return path

        return build

    with (
        patch('tools.release.arch.package', side_effect=native('slopworld-1.0.0-x86_64.pkg.tar.zst')),
        patch('tools.release.container_debian.package', side_effect=native('slopworld-1.0.0-amd64.deb')),
    ):
        assets = publish.package(output, 'revision', '1.0.0', native_packages=True)
    names = [path.name for path in assets]
    assert names[-3:] == ['slopworld-1.0.0-x86_64.pkg.tar.zst', 'slopworld-1.0.0-amd64.deb', 'SHA256SUMS']
    assert len(assets[-1].read_text().splitlines()) == 4
    for line in assets[-1].read_text().splitlines():
        digest, name = line.split('  ')
        assert digest == hashlib.sha256((output / name).read_bytes()).hexdigest()


def test_native_package_failure_preserves_entire_previous_release(inputs: Path, tmp_path: Path) -> None:
    output = tmp_path / 'release'
    assets = publish.package(output, 'revision', '1.0.0')
    previous = [path.read_bytes() for path in assets]
    with patch('tools.release.arch.package', side_effect=ValueError('native build failed')):
        with pytest.raises(ValueError, match='native build failed'):
            publish.package(output, 'new revision', '1.0.1', native_packages=True)
    assert [path.read_bytes() for path in assets] == previous
