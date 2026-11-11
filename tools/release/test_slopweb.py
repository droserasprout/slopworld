"""Guard package boundaries, install ownership, and private runtime preparation."""

import argparse
import fcntl
import hashlib
import importlib.util
import json
import shutil
import struct
import subprocess
import zipfile
from pathlib import Path
from typing import Any

import pytest

from tools import ROOT
from tools.release import slopweb


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
    assert env['SLOPD_TOKEN'] == 'fixture-secret'
    assert env['SLOPD_URL'] == 'http://127.0.0.1:7717'
    token = tmp_path / 'token'
    token.write_text('override\n')
    args.token_file = str(token)
    assert launcher.environment(args)['SLOPD_TOKEN'] == 'override'
    assert descriptor.read_text().endswith('"fixture-secret"\n')


def test_font_preparation_filters_profile_and_uses_private_native_bakes(
    launcher: Any, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    import appearance

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
    assert [face['bake'] for face in manifest] == [
        f'{face}-style{style}@{density}.json'
        for density in ('1', '1.66667', '2')
        for style in (0, 1, 2, 3)
        for face in ('ui-13', 'ui-15', 'ui-17', 'terminal')
    ]
    assert len(calls) == 50
    assert [float(call[0][-2]) for call in calls[2:]] == [density for density in (1, 5 / 3, 2) for _ in range(16)]
    assert all(call[1]['check'] for call in calls)
    assert all(call[1]['env']['LD_LIBRARY_PATH'].startswith(str(tmp_path / 'game')) for call in calls)
    assert 'private' not in (root / 'assets/reference-appearance.json').read_text()


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
    launcher: Any, tmp_path: Path, monkeypatch: pytest.MonkeyPatch, values: dict, sizes: list[int]
) -> None:
    import appearance

    calls = []
    monkeypatch.setattr(appearance.subprocess, 'run', lambda args, **kwargs: calls.append(args))
    appearance.prepare_fonts(tmp_path / 'bundle', tmp_path / 'game', tmp_path / 'runtime', values)
    assert [int(call[-3]) for call in calls[2:]] == sizes * 12


def test_portrait_snapshot_preserves_pixels_and_identity_without_host_metadata(launcher: Any, tmp_path: Path) -> None:
    import portraits

    source = tmp_path / 'native'
    source.mkdir()
    pixels = struct.pack('<ii', 2, 1) + bytes([11, 22, 33, 255, 44, 55, 66, 128])
    name = hashlib.sha256(pixels).hexdigest() + '.rgba'
    (source / name).write_bytes(pixels)
    manifest = {
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
    import portraits

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
    import portraits

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
    launcher: Any, tmp_path: Path, change: dict
) -> None:
    import portraits

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
    game.mkdir()
    for name in ('RimWorld.dll', 'Assembly-CSharp.dll', 'UnityStubs.dll', 'Waylith.dll', 'Assets.zip'):
        (game / name).write_text('private game input')
    calls: list[list[str]] = []

    def run(args: list[str], **kwargs: Any) -> None:
        calls.append(args)

    monkeypatch.setattr(launcher.subprocess, 'run', run)
    monkeypatch.setattr(launcher, 'preferences', dict)
    monkeypatch.setattr(launcher, 'prepare_fonts', lambda *args: None)
    ready = launcher.prepare(bundle, game, cache)
    assert (ready / '.ready').is_file()
    assert len(calls) == 5
    assert launcher.prepare(bundle, game, cache) == ready
    assert len(calls) == 5
    refreshed = launcher.prepare(bundle, game, cache, True)
    assert refreshed != ready and (ready / '.ready').is_file()
    assert launcher.prepare(bundle, game, cache) == refreshed

    # Updated browser tooling must not reuse a ready runtime patched by an older
    # bundle, even when all installed game inputs and preferences are unchanged.
    (bundle / 'BUILD_ID').write_text('updated compatibility tooling')
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


def test_distributable_build_never_reads_staged_game_root(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    root = tmp_path / 'source'
    runtime = root / 'experiments/browser/runtime'
    inputs = [
        'slopweb/launch.py',
        'slopweb/aot.py',
        'slopweb/appearance.py',
        'slopweb/portraits.py',
        'experiments/browser/runtime/FontProbe/bin/Debug/net10.0/FontProbe.dll',
        'experiments/browser/runtime/FontProbe/bin/Debug/net10.0/FontProbe.deps.json',
        'experiments/browser/runtime/FontProbe/bin/Debug/net10.0/FontProbe.runtimeconfig.json',
        'experiments/browser/runtime/BackgroundProbe/bin/Debug/net10.0/BackgroundProbe.dll',
        'experiments/browser/runtime/BackgroundProbe/bin/Debug/net10.0/BackgroundProbe.deps.json',
        'experiments/browser/runtime/BackgroundProbe/bin/Debug/net10.0/BackgroundProbe.runtimeconfig.json',
        'experiments/browser/runtime/core_pack.py',
        'experiments/browser/runtime/bin/Debug/net10.0/Probe.dll',
        'experiments/browser/runtime/Adapters/bin/Debug/net10.0/Adapters.dll',
        'experiments/browser/runtime/bin/Debug/net10.0/wwwroot/_framework/dotnet.js',
        'experiments/browser/runtime/wwwroot/index.html',
        'experiments/browser/runtime/Scanner/bin/Debug/net10.0/Scanner.dll',
        'experiments/browser/compat/Offline/bin/Debug/net10.0/Offline.dll',
        'experiments/browser/runtime/out/wwwroot-eco/mod/Content.zip',
        'mod/Textures/SlopWorld/LoadingFont.png',
        'shared/loading-tips.txt',
    ]
    inputs += [
        f'mod/Assemblies/{name}.dll'
        for name in ('SlopWorld', '0Harmony', 'Google.Protobuf', 'Markdig', 'AngleSharp', 'Tomlyn')
    ]
    inputs += [f'experiments/browser/harness/{name}.mjs' for name in ('server', 'bridge', 'websocket')]
    for name in inputs:
        path = root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text('redistributable fixture')
    shutil.copyfile(ROOT / 'experiments/browser/runtime/Probe.csproj', runtime / 'Probe.csproj')
    for name in slopweb.AOT_SOURCES:
        (runtime / name).write_text('authored C# fixture')
    for name in ('unlisted.cs', 'packages.lock.json', 'private.dll'):
        (runtime / name).write_text('must never ship')
    with zipfile.ZipFile(runtime / 'out/wwwroot-eco/mod/Content.zip', 'w') as content:
        content.writestr('Textures/face.rgba', b'authored UI pixels')
    monkeypatch.setattr(slopweb, 'tracked_mod_files', lambda: {'Textures/face.png'})
    package = tmp_path / 'nuget/microsoft.netcore.app.runtime.mono.browser-wasm/10.0.14'
    package.mkdir(parents=True)
    for name in ('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT'):
        (package / name).write_text('runtime notice')
    (runtime / 'obj').mkdir()
    (runtime / 'obj/project.assets.json').write_text(
        json.dumps(
            {
                'packageFolders': {str(tmp_path / 'nuget'): {}},
                'project': {
                    'frameworks': {
                        'net10.0': {
                            'downloadDependencies': [
                                {
                                    'name': 'Microsoft.NETCore.App.Runtime.Mono.browser-wasm',
                                    'version': '[10.0.14, 10.0.14]',
                                }
                            ]
                        }
                    }
                },
            }
        )
    )
    for name in (
        'game/RimWorld.dll',
        'assets/Core.zip',
        'assets/Assets.zip',
        'assets/portraits/private.rgba',
        'credentials.toml',
    ):
        path = runtime / 'out/wwwroot-eco' / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text('must never ship')
    (runtime / 'FontProbe/bin/Debug/net10.0/Brutal.ImGui.dll').write_text('must never ship')
    monkeypatch.setattr(slopweb, 'ROOT', root)
    output = tmp_path / 'bundle'
    slopweb.build(output)
    assert (output / 'BUILD_ID').is_file()
    assert {path.name for path in (output / 'aot-src').iterdir()} == {'Probe.csproj', *slopweb.AOT_SOURCES}
    assert (output / 'aot.py').is_file()
    assert all(b'must never ship' not in path.read_bytes() for path in output.rglob('*') if path.is_file())
    assert not (output / 'game').exists()
    assert not (output / 'web/assets').exists()


def test_package_rejects_untracked_assets_inside_content_archive(tmp_path: Path) -> None:
    archive = tmp_path / 'Content.zip'
    with zipfile.ZipFile(archive, 'w') as content:
        content.writestr('Textures/private.rgba', b'local-only pixels')
    with pytest.raises(ValueError, match='not a tracked mod asset'):
        slopweb.validate_content(archive, {'Textures/authored.png'})


@pytest.mark.parametrize('distribution', ['arch', 'debian', 'fedora'])
def test_package_formats_stage_only_web_payload(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, distribution: str
) -> None:
    from tools.release import arch
    from tools.release import debian
    from tools.release import fedora

    module = {'arch': arch, 'debian': debian, 'fedora': fedora}[distribution]
    bundle = tmp_path / 'bundle'
    bundle.mkdir()
    (bundle / 'BUILD_ID').write_text('fixture')
    (bundle / 'launch.py').write_text('fixture')
    monkeypatch.setenv('SOURCE_DATE_EPOCH', '1234567890')
    metadata = []

    def run(args: list[str], **kwargs: Any) -> subprocess.CompletedProcess[str]:
        assert args[0] != 'dpkg-shlibdeps', 'The Python launcher has no ELF dependency scan'
        if args[0] == 'dpkg-deb':
            payload, output = Path(args[-2]), Path(args[-1])
            metadata.append((payload / 'DEBIAN/control').read_text())
        elif args[0] == 'rpmbuild':
            spec = Path(args[-1])
            metadata.append(spec.read_text())
            workspace = spec.parent.parent
            payload = workspace / 'SOURCES/payload'
            output = workspace / 'RPMS/slopweb-1.2.3-1.x86_64.rpm'
        elif 'makepkg' in args:
            workspace = Path(kwargs['cwd'])
            assert 'PKGBUILD.slopweb' in (workspace / 'PKGBUILD').read_text()
            metadata.append((ROOT / 'packaging/arch/PKGBUILD.slopweb').read_text())
            payload = workspace / 'payload'
            output = workspace / 'slopweb-1.2.3-1-x86_64.pkg.tar.zst'
        else:
            return subprocess.CompletedProcess(args, 0, '')
        assert sorted(path.name for path in (payload / 'usr/bin').iterdir()) == ['slopweb']
        assert not (payload / 'usr/share/slopworld').exists()
        assert not (payload / 'usr/lib/systemd').exists()
        assert (payload / 'usr/share/slopweb/BUILD_ID').read_text() == 'fixture'
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_bytes(b'fixture package')
        return subprocess.CompletedProcess(args, 0, '')

    monkeypatch.setattr(module, 'run', run)
    result = module.package(
        tmp_path / 'output',
        'revision',
        '1.2.3',
        'amd64' if distribution == 'debian' else 'x86_64',
        bundle,
        product='slopweb',
    )
    assert result.read_bytes() == b'fixture package'
    assert 'nodejs' in metadata[0] and 'python' in metadata[0] and 'dotnet-runtime' in metadata[0]


def test_uninstall_preserves_unrelated_files_and_private_cache(tmp_path: Path) -> None:
    for name in ('bin/slopweb', 'bin/slopd', 'share/slopweb/launch.py', '.cache/slopweb/game.dll'):
        path = tmp_path / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text('fixture')
    slopweb.install(tmp_path, uninstall=True)
    assert not (tmp_path / 'bin/slopweb').exists()
    assert not (tmp_path / 'share/slopweb').exists()
    assert (tmp_path / 'bin/slopd').is_file()
    assert (tmp_path / '.cache/slopweb/game.dll').is_file()


def test_real_arch_archive_contains_only_distributable_web_payload(tmp_path: Path) -> None:
    archives = list((ROOT / 'dist/arch').glob('slopweb-*.pkg.tar.zst'))
    if not archives or not shutil.which('tar') or not shutil.which('zstd'):
        pytest.skip('Build an Arch slopweb package to check its real archive')
    archive = max(archives, key=lambda path: path.stat().st_mtime_ns)
    names = subprocess.run(
        ['tar', '--zstd', '-tf', str(archive)], check=True, capture_output=True, text=True
    ).stdout.splitlines()
    forbidden = {
        'RimWorld.dll',
        'Assembly-CSharp.dll',
        'UnityStubs.dll',
        'Waylith.dll',
        'Assets.zip',
        'Core.zip',
        'Brutal.ImGui.dll',
        'Brutal.Core.Numerics.dll',
        'ui.ttf',
        'terminal.ttf',
        '.ready',
    }
    assert all(Path(name).name not in forbidden for name in names)
    assert 'usr/bin/slopweb' in names
    assert 'usr/share/slopweb/licenses/dotnet/THIRD-PARTY-NOTICES.TXT' in names
    for helper, directory in (('FontProbe', 'font-probe'), ('BackgroundProbe', 'background-probe')):
        assert f'usr/share/slopweb/{directory}/{helper}.dll' in names
    assert not any('/assets/fonts/' in name or '/assets/background/' in name for name in names)
    assert not any(name.startswith(('usr/share/slopworld/', 'usr/lib/systemd/')) for name in names)
    # Execute the actual archived launcher and relative install layout without
    # touching the installed copy or requiring proprietary game data.
    subprocess.run(['tar', '--zstd', '-xf', str(archive), '-C', str(tmp_path)], check=True)
    result = subprocess.run([str(tmp_path / 'usr/bin/slopweb'), '--help'], check=True, capture_output=True, text=True)
    assert '--prepare' in result.stdout and '--token-file' in result.stdout


@pytest.mark.parametrize('change', ['extra', 'missing', 'duplicate', 'symlink'])
def test_aot_source_payload_requires_reviewed_compile_set(tmp_path: Path, change: str) -> None:
    import xml.etree.ElementTree as ET

    runtime = tmp_path / 'runtime'
    runtime.mkdir()
    source = ROOT / 'experiments/browser/runtime'
    shutil.copyfile(source / 'Probe.csproj', runtime / 'Probe.csproj')
    for name in slopweb.AOT_SOURCES:
        (runtime / name).write_text('authored source')
    if change == 'symlink':
        (runtime / 'Program.cs').unlink()
        (runtime / 'Program.cs').symlink_to(source / 'Program.cs')
    else:
        project = ET.parse(runtime / 'Probe.csproj')
        group = project.find('.//ItemGroup')
        assert group is not None
        if change == 'extra':
            ET.SubElement(group, 'Compile', Include='../private.cs')
        elif change == 'duplicate':
            ET.SubElement(group, 'Compile', Include='Program.cs')
        else:
            item = group.find('Compile')
            assert item is not None
            group.remove(item)
        project.write(runtime / 'Probe.csproj')
    with pytest.raises(ValueError, match=r'allowlist|symlink'):
        slopweb.stage_aot_sources(runtime, tmp_path / 'bundle')


def test_explicit_portrait_source_is_validated_and_never_requests_live_capture(launcher, tmp_path, monkeypatch):
    import portraits

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


def test_missing_portrait_snapshot_can_be_imported_again(launcher, tmp_path):
    import portraits

    snapshot = portraits.read_snapshot(tmp_path / 'missing')
    snapshot.write(tmp_path)
    assert portraits.read_snapshot(tmp_path / 'assets/portraits').identity == snapshot.identity
    assert json.loads(snapshot.manifest)['entries'] == []
