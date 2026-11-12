"""Guard host credentials, private runtime preparation, and presentation inputs."""

import argparse
import fcntl
import hashlib
import importlib.util
import json
import struct
import subprocess
from pathlib import Path
from typing import Any

import pytest

from tools import ROOT


@pytest.fixture
def launcher(monkeypatch: pytest.MonkeyPatch) -> Any:
    monkeypatch.syspath_prepend(str(ROOT / 'slopweb'))
    spec = importlib.util.spec_from_file_location('slopweb_launch', ROOT / 'slopweb/launch.py')
    assert spec and spec.loader
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def test_endpoint_credentials_stay_in_host_environment(
    launcher: Any, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    descriptor = tmp_path / 'local.toml'
    descriptor.write_text('url = "http://127.0.0.1:7717"\ntoken = "fixture-secret"\n')
    monkeypatch.setenv('SLOPD_ENDPOINT', str(descriptor))
    monkeypatch.delenv('SLOPD_URL', raising=False)
    monkeypatch.delenv('SLOPD_TOKEN', raising=False)
    args = argparse.Namespace(endpoint=None, token_file=None)
    env = launcher.environment(args)
    assert env['SLOPWEB_ENDPOINT_FILE'] == str(descriptor)
    assert 'SLOPD_TOKEN' not in env
    assert launcher.connection(env)['token'] == 'fixture-secret'
    monkeypatch.setenv('SLOPD_URL', 'http://other.invalid')
    with pytest.raises(ValueError, match='together'):
        launcher.environment(args)
    monkeypatch.setenv('SLOPD_TOKEN', 'paired')
    assert launcher.connection(launcher.environment(args))['token'] == 'paired'
    monkeypatch.setenv('SLOPD_CONFIG_ROOT', str(tmp_path))
    (tmp_path / 'endpoints').mkdir()
    (tmp_path / 'endpoints/remote.toml').write_text('url = "https://remote.invalid"\ntoken = "remote"\n')
    args.endpoint = 'remote'
    assert launcher.connection(launcher.environment(args)) == {'endpoint': 'https://remote.invalid', 'token': 'remote'}
    args.endpoint = '../local'
    with pytest.raises(ValueError, match='endpoint name'):
        launcher.environment(args)
    args.endpoint = None
    args.token_file = str(tmp_path / 'token')
    Path(args.token_file).write_text('explicit')
    with pytest.raises(ValueError, match='together'):
        launcher.environment(args)
    args.url = 'https://explicit.invalid'
    assert launcher.connection(launcher.environment(args))['token'] == 'explicit'
    assert descriptor.read_text().endswith('"fixture-secret"\n')


def test_font_preparation_filters_profile_and_uses_private_native_bakes(
    launcher: Any, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    appearance = importlib.import_module('appearance')

    profile = tmp_path / 'slopworld/profiles/local/Config/SlopWorld.toml'
    profile.parent.mkdir(parents=True)
    profile.write_text('uiFontSize = 15\nfontSize = 14\nfontName = "monospace"\ntoken = "private"\n')
    monkeypatch.setenv('XDG_DATA_HOME', str(tmp_path))
    values = appearance.preferences()
    assert 'token' not in values
    assert values['hostHome'] == str(Path.home())
    calls = []
    monkeypatch.setattr(appearance.subprocess, 'run', lambda args, **kwargs: calls.append((args, kwargs)))
    root = tmp_path / 'private-runtime'
    appearance.prepare_fonts(tmp_path / 'bundle', tmp_path / 'game', root, values)
    manifest = json.loads((root / 'assets/fonts/manifest.json').read_text())
    assert [face['bake'] for face in manifest] == ['ui-13.json', 'ui-15.json', 'ui-17.json', 'terminal.json']
    assert len(calls) == 7
    assert all(float(call[0][-2]) == 1 for call in calls[3:])
    assert all(int(call[0][-1]) == 0 for call in calls[3:])
    assert all(call[1]['check'] for call in calls)
    assert all(call[1]['env']['LD_LIBRARY_PATH'].startswith(str(tmp_path / 'game')) for call in calls)
    assert 'private' not in (root / 'assets/reference-appearance.json').read_text()


def test_startup_font_failure_falls_back_without_changing_host_preferences(
    launcher: Any, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    appearance = importlib.import_module('appearance')
    values = {'uiFontName': 'Removed font', 'fontName': 'monospace', 'uiFontSize': 15}
    calls = []

    def probe(args: list[str], **kwargs: object) -> None:
        calls.append(args)
        if args[3] == '--resolve-ui' and args[5] == 'Removed font':
            raise subprocess.CalledProcessError(1, args)

    monkeypatch.setattr(appearance.subprocess, 'run', probe)
    root = tmp_path / 'runtime'
    appearance.prepare_fonts(tmp_path / 'bundle', tmp_path / 'game', root, values)
    assert [args[5] for args in calls if args[3] == '--resolve-ui'] == ['Removed font', '']
    published = json.loads((root / 'assets/reference-appearance.json').read_text())
    assert published == {**values, 'uiFontName': ''}
    assert values['uiFontName'] == 'Removed font'
    assert len(json.loads((root / 'assets/fonts/manifest.json').read_text())) == 4


@pytest.mark.parametrize('family', ['', 'Removed font'])
def test_startup_font_failure_without_usable_defaults_does_not_publish(
    launcher: Any, tmp_path: Path, monkeypatch: pytest.MonkeyPatch, family: str
) -> None:
    appearance = importlib.import_module('appearance')
    calls = []

    def probe(args: list[str], **kwargs: object) -> None:
        if args[3] == '--resolve-ui':
            calls.append(args[5])
            raise subprocess.CalledProcessError(1, args)

    monkeypatch.setattr(appearance.subprocess, 'run', probe)
    root = tmp_path / 'runtime'
    with pytest.raises(subprocess.CalledProcessError):
        appearance.prepare_fonts(tmp_path / 'bundle', tmp_path / 'game', root, {'uiFontName': family})
    assert calls == ([family, ''] if family else [''])
    assert not (root / 'assets/reference-appearance.json').exists()
    assert not (root / 'assets/fonts/manifest.json').exists()


@pytest.mark.parametrize(
    'values, sizes',
    [
        ({}, [12, 14, 16, 14]),
        ({'uiFontSize': 0}, [11, 13, 15, 14]),
        ({'uiFontSize': -1, 'fontSize': 0}, [11, 13, 15, 8]),
        ({'uiFontSize': 1, 'fontSize': -1}, [1, 1, 3, 8]),
        ({'uiFontSize': 99, 'fontSize': 99}, [46, 48, 50, 28]),
    ],
)
def test_font_preparation_matches_native_defaults_and_size_limits(
    launcher: Any, tmp_path: Path, monkeypatch: pytest.MonkeyPatch, values: dict[str, Any], sizes: list[int]
) -> None:
    appearance = importlib.import_module('appearance')

    calls = []
    monkeypatch.setattr(appearance.subprocess, 'run', lambda args, **kwargs: calls.append(args))
    appearance.prepare_fonts(tmp_path / 'bundle', tmp_path / 'game', tmp_path / 'runtime', values)
    assert [int(call[-3]) for call in calls[3:]] == sizes


def test_portrait_snapshot_preserves_pixels_and_identity_without_host_metadata(launcher: Any, tmp_path: Path) -> None:
    portraits = importlib.import_module('portraits')

    source = tmp_path / 'native'
    source.mkdir()
    pixels = struct.pack('<ii', 2, 1) + bytes([11, 22, 33, 255, 44, 55, 66, 128])
    name = hashlib.sha256(pixels).hexdigest() + '.rgba'
    (source / name).write_bytes(pixels)
    manifest: dict[str, Any] = {
        'version': 1,
        'uiScale': 2,
        'private': 'must-not-copy',
        'entries': [{'session': 'agent-a', 'file': name, 'width': 2, 'height': 1, 'token': 'must-not-copy'}],
    }
    (source / 'manifest.json').write_text(json.dumps(manifest))
    snapshot = portraits.read_snapshot(source)
    assert snapshot.files[name] == pixels
    assert b'must-not-copy' not in snapshot.manifest
    root = tmp_path / 'runtime'
    snapshot.write(root)
    assert (root / 'assets/portraits' / name).read_bytes() == pixels
    assert (root / 'assets/portraits/manifest.json').read_bytes() == snapshot.manifest
    first = snapshot.identity
    manifest['entries'][0]['session'] = 'renamed-agent'
    (source / 'manifest.json').write_text(json.dumps(manifest))
    assert portraits.read_snapshot(source).identity != first
    (source / name).write_bytes(pixels[:-1])
    with pytest.raises(ValueError, match='truncated or changed'):
        portraits.read_snapshot(source)


def test_portrait_request_ignores_stale_ack_and_reads_only_published_snapshot(
    launcher: Any, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    portraits = importlib.import_module('portraits')

    now = [0.0]
    monkeypatch.setattr(portraits.time, 'monotonic', lambda: now[0])
    (tmp_path / 'response').write_text(json.dumps({'version': 1, 'id': 'old', 'ok': True}))

    def native_poll(delay: float) -> None:
        now[0] += delay
        identity = (tmp_path / 'request').read_text()
        assert len(identity) == 32
        (tmp_path / 'manifest.json').write_text(json.dumps({'version': 1, 'uiScale': 2, 'entries': []}))
        (tmp_path / 'response').write_text(json.dumps({'version': 1, 'id': identity, 'ok': True}))

    monkeypatch.setattr(portraits.time, 'sleep', native_poll)
    snapshot = portraits.request_snapshot(tmp_path)
    assert json.loads(snapshot.manifest)['uiScale'] == 2
    assert now[0] > 0
    assert not (tmp_path / 'request').exists()


@pytest.mark.parametrize('failure', ['timeout', 'native', 'missing_snapshot', 'locked'])
def test_portrait_request_failure_is_bounded_and_preserves_other_requests(
    launcher: Any, tmp_path: Path, monkeypatch: pytest.MonkeyPatch, failure: str
) -> None:
    portraits = importlib.import_module('portraits')

    now = [0.0]
    monkeypatch.setattr(portraits.time, 'monotonic', lambda: now[0])

    def native_poll(delay: float) -> None:
        now[0] += delay
        if failure in ('native', 'missing_snapshot'):
            identity = (tmp_path / 'request').read_text()
            (tmp_path / 'response').write_text(
                json.dumps(
                    {'version': 1, 'id': identity, 'ok': failure == 'missing_snapshot', 'error': 'readback failed'}
                )
            )

    monkeypatch.setattr(portraits.time, 'sleep', native_poll)
    with (tmp_path / '.request.lock').open('a') as lock:
        if failure == 'locked':
            fcntl.flock(lock, fcntl.LOCK_EX)
            (tmp_path / 'request').write_text('another-owner')
        with pytest.raises((TimeoutError, RuntimeError)):
            portraits.request_snapshot(tmp_path, timeout=0.2)
    assert now[0] <= 0.2
    if failure == 'locked':
        assert (tmp_path / 'request').read_text() == 'another-owner'
    else:
        assert not (tmp_path / 'request').exists()


@pytest.mark.parametrize(
    'change',
    [
        {'file': '../secret'},
        {'width': 4096},
        {'width': True},
        {'height': 2},
        {'session': ''},
    ],
)
def test_portrait_snapshot_rejects_unsafe_paths_and_inconsistent_geometry(
    launcher: Any, tmp_path: Path, change: dict[str, Any]
) -> None:
    portraits = importlib.import_module('portraits')

    pixels = struct.pack('<ii', 1, 1) + bytes([0, 0, 0, 255])
    name = hashlib.sha256(pixels).hexdigest() + '.rgba'
    (tmp_path / name).write_bytes(pixels)
    entry = {'session': 'agent', 'file': name, 'width': 1, 'height': 1, **change}
    (tmp_path / 'manifest.json').write_text(json.dumps({'version': 1, 'uiScale': 2, 'entries': [entry]}))
    with pytest.raises(ValueError):
        portraits.read_snapshot(tmp_path)


def test_failed_prepare_preserves_ready_runtime_and_does_not_publish_partial_files(
    launcher: Any, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    bundle, game, cache = (tmp_path / name for name in ('bundle', 'game', 'cache'))
    (bundle / 'web').mkdir(parents=True)
    (bundle / 'web/index.html').write_text('fixture')
    (bundle / 'BUILD_ID').write_text('build')
    (bundle / 'Adapters.dll').write_text('our adapter')
    (bundle / 'mod').mkdir()
    (bundle / 'ost').mkdir()
    (bundle / 'ost/pace-20261012.ogg').write_bytes(b'authored music')
    game.mkdir()
    for name in ('RimWorld.dll', 'Assembly-CSharp.dll', 'UnityStubs.dll', 'Waylith.dll', 'Assets.zip'):
        (game / name).write_text('private game input')
    calls: list[list[str]] = []

    def run(args: list[str], **kwargs: Any) -> None:
        calls.append(args)

    monkeypatch.setattr(launcher.subprocess, 'run', run)
    monkeypatch.setattr(launcher, 'preferences', dict)
    font_calls = []

    def prepare_fonts(bundle: Path, game: Path, root: Path, values: dict[str, object]) -> None:
        font_calls.append(values)
        launcher.write_preferences(root, values)

    monkeypatch.setattr(launcher, 'prepare_fonts', prepare_fonts)
    ready = launcher.prepare(bundle, game, cache)
    assert (ready / '.ready').is_file()
    assert (ready / 'assets/ost/pace-20261012.ogg').read_bytes() == b'authored music'
    assert not (ready / 'assets/portraits').exists()
    assert len(calls) == 5
    assert launcher.prepare(bundle, game, cache) == ready
    assert len(calls) == 5
    # Presentation and browser assets compose without rerunning native patchers.
    (bundle / 'web/index.html').write_text('updated browser asset')
    browser = launcher.prepare(bundle, game, cache)
    assert browser != ready
    assert len(calls) == 5
    monkeypatch.setattr(launcher, 'preferences', lambda: {'sidebarHidden': True})
    presentation = launcher.prepare(bundle, game, cache)
    assert presentation != browser
    assert len(calls) == 5
    assert json.loads((presentation / 'assets/reference-appearance.json').read_text())['sidebarHidden']
    assert len(font_calls) == 1
    monkeypatch.setattr(launcher, 'preferences', lambda: {'sidebarHidden': True, 'fontSize': 18})
    changed_font = launcher.prepare(bundle, game, cache)
    assert len(font_calls) == 2 and len(calls) == 5
    assert json.loads((changed_font / 'assets/reference-appearance.json').read_text())['fontSize'] == 18
    # The archive is owned separately from the patched assemblies.
    (game / 'Data/Core').mkdir(parents=True)
    (game / 'Data/Core/data.xml').write_text('new game data')
    launcher.prepare(bundle, game, cache)
    assert len(calls) == 6
    assert calls[-1][1].endswith('core_pack.py')
    refreshed = launcher.prepare(bundle, game, cache, True)
    assert refreshed != ready and (ready / '.ready').is_file()
    assert launcher.prepare(bundle, game, cache) == refreshed
    assert len(font_calls) == 3
    (bundle / 'web/index.html').write_text('another browser update')
    launcher.prepare(bundle, game, cache)
    assert len(font_calls) == 3

    # Updated browser tooling must not reuse a ready runtime patched by an older
    # bundle, even when all installed game inputs and preferences are unchanged.
    (bundle / 'Adapters.dll').write_text('updated compatibility tooling')
    upgraded = launcher.prepare(bundle, game, cache)
    assert upgraded not in (ready, refreshed)
    assert (refreshed / '.ready').is_file()
    assert launcher.prepare(bundle, game, cache) == upgraded

    def fail(args: list[str], **kwargs: Any) -> None:
        raise subprocess.CalledProcessError(1, args)

    monkeypatch.setattr(launcher.subprocess, 'run', fail)
    with pytest.raises(subprocess.CalledProcessError):
        launcher.prepare(bundle, game, cache, True)
    assert (ready / '.ready').is_file()
    assert launcher.prepare(bundle, game, cache) == upgraded
    assert not list(cache.glob('.prepare-*'))


def test_explicit_portrait_source_is_validated_and_never_requests_live_capture(
    launcher: Any, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    portraits = importlib.import_module('portraits')

    monkeypatch.setenv('SLOPWEB_PORTRAITS', '')
    with pytest.raises(ValueError, match='cannot be empty'):
        portraits.host_snapshot()
    monkeypatch.setenv('SLOPWEB_PORTRAITS', str(tmp_path))
    monkeypatch.setattr(portraits, 'request_snapshot', lambda *args: pytest.fail('Unexpected live capture'))
    with pytest.raises(ValueError, match='Explicit portrait source'):
        portraits.host_snapshot()
    (tmp_path / 'manifest.json').write_text('{"version":1,"uiScale":2,"entries":[]}')
    snapshot = portraits.host_snapshot()
    assert json.loads(snapshot.manifest)['uiScale'] == 2
    (tmp_path / 'manifest.json').write_text('{"version":2,"entries":[]}')
    with pytest.raises(ValueError, match='Invalid portrait manifest'):
        portraits.host_snapshot()


def test_missing_portrait_snapshot_can_be_imported_again(launcher: Any, tmp_path: Path) -> None:
    portraits = importlib.import_module('portraits')

    snapshot = portraits.read_snapshot(tmp_path / 'missing')
    snapshot.write(tmp_path)
    assert portraits.read_snapshot(tmp_path / 'assets/portraits').identity == snapshot.identity
    assert json.loads(snapshot.manifest)['entries'] == []


def test_browser_ost_catalog_matches_authored_songs_and_files() -> None:
    import re
    import xml.etree.ElementTree as ET

    source = (ROOT / 'slopweb/runtime/Adapters/BrowserAudio.cs').read_text()
    tracks = re.findall(r'"([a-z]+-\d{8})"', source)
    definitions = ET.parse(ROOT / 'mod/Defs/Songs.xml').getroot()
    songs = []
    for node in definitions.findall('./SongDef/clipPath'):
        assert node.text is not None
        songs.append(node.text.rsplit('/', 1)[-1])
    assert tracks == songs
    assert sorted(tracks) == sorted(path.stem for path in (ROOT / 'mod/Sounds/SlopWorld/OST').glob('*.ogg'))


def test_prepare_uses_selected_profile_without_contacting_endpoint(
    launcher: Any, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    game = tmp_path / 'game'
    game.mkdir()
    (game / 'RimWorld').touch()
    monkeypatch.setenv('SLOPD_DATA', str(tmp_path / 'data'))
    monkeypatch.setenv('SLOPD_ENDPOINT', str(tmp_path / 'missing.toml'))
    monkeypatch.delenv('SLOPWORLD_PROFILE', raising=False)
    monkeypatch.setattr('sys.argv', ['slopweb', '--prepare', '--game', str(game), '--endpoint', 'remote'])
    selected = []
    monkeypatch.setattr(launcher, 'prepare', lambda *args: selected.append(args) or tmp_path / 'runtime')
    monkeypatch.setattr(launcher, 'environment', lambda *args: pytest.fail('Preparation must remain offline'))
    monkeypatch.setattr(launcher.aot, 'toolchain', lambda sdk: {})
    monkeypatch.setattr(launcher.aot, 'prepare', lambda bundle, root, cache, sdk: root)
    monkeypatch.setattr(launcher.shutil, 'which', lambda name: '/sdk/dotnet')
    launcher.main()
    assert selected[0][-1] == tmp_path / 'data/profiles/remote'
