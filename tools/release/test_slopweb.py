"""Guard package boundaries, install ownership, and private runtime preparation."""

import argparse
import importlib.util
import json
import shutil
import subprocess
import zipfile
from pathlib import Path
from typing import Any

import pytest

from tools import ROOT
from tools.release import slopweb


@pytest.fixture
def launcher() -> Any:
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
    ready = launcher.prepare(bundle, game, cache)
    assert (ready / '.ready').is_file()
    assert len(calls) == 4
    assert launcher.prepare(bundle, game, cache) == ready
    assert len(calls) == 4
    refreshed = launcher.prepare(bundle, game, cache, True)
    assert refreshed != ready and (ready / '.ready').is_file()
    assert launcher.prepare(bundle, game, cache) == refreshed

    def fail(args: list[str], **kwargs: Any) -> None:
        raise subprocess.CalledProcessError(1, args)

    monkeypatch.setattr(launcher.subprocess, 'run', fail)
    with pytest.raises(subprocess.CalledProcessError):
        launcher.prepare(bundle, game, cache, True)
    assert (ready / '.ready').is_file()
    assert launcher.prepare(bundle, game, cache) == refreshed
    assert not list(cache.glob('.prepare-*'))


def test_distributable_build_never_reads_staged_game_root(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    root = tmp_path / 'source'
    runtime = root / 'experiments/browser/runtime'
    inputs = [
        'slopweb/launch.py',
        'experiments/browser/runtime/core_pack.py',
        'experiments/browser/runtime/bin/Debug/net10.0/Probe.dll',
        'experiments/browser/runtime/Adapters/bin/Debug/net10.0/Adapters.dll',
        'experiments/browser/runtime/bin/Debug/net10.0/wwwroot/_framework/dotnet.js',
        'experiments/browser/runtime/wwwroot/index.html',
        'experiments/browser/runtime/Scanner/bin/Debug/net10.0/Scanner.dll',
        'experiments/browser/compat/Offline/bin/Debug/net10.0/Offline.dll',
        'experiments/browser/runtime/out/wwwroot-eco/mod/Content.zip',
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
    for name in ('game/RimWorld.dll', 'assets/Core.zip', 'assets/Assets.zip', 'credentials.toml'):
        path = runtime / 'out/wwwroot-eco' / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text('must never ship')
    monkeypatch.setattr(slopweb, 'ROOT', root)
    output = tmp_path / 'bundle'
    slopweb.build(output)
    assert (output / 'BUILD_ID').is_file()
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
        '.ready',
    }
    assert all(Path(name).name not in forbidden for name in names)
    assert 'usr/bin/slopweb' in names
    assert 'usr/share/slopweb/licenses/dotnet/THIRD-PARTY-NOTICES.TXT' in names
    assert not any(name.startswith(('usr/share/slopworld/', 'usr/lib/systemd/')) for name in names)
    # Execute the actual archived launcher and relative install layout without
    # touching the installed copy or requiring proprietary game data.
    subprocess.run(['tar', '--zstd', '-xf', str(archive), '-C', str(tmp_path)], check=True)
    result = subprocess.run([str(tmp_path / 'usr/bin/slopweb'), '--help'], check=True, capture_output=True, text=True)
    assert '--prepare' in result.stdout and '--token-file' in result.stdout
