"""Guard package boundaries and install ownership."""

import json
import shutil
import subprocess
import zipfile
from pathlib import Path
from typing import Any

import pytest

from tools import ROOT
from tools.release import slopweb


@pytest.mark.parametrize('private_runtime', [False, True])
def test_distributable_build_never_reads_staged_game_root(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, private_runtime: bool
) -> None:
    root = tmp_path / 'source'
    runtime = root / 'slopweb/runtime'
    inputs = [
        'slopweb/launch.py',
        'slopweb/aot.py',
        'slopweb/appearance.py',
        'slopweb/fonts.py',
        'slopweb/portraits.py',
        'slopweb/runtime/FontProbe/bin/Debug/net10.0/FontProbe.dll',
        'slopweb/runtime/FontProbe/bin/Debug/net10.0/FontProbe.deps.json',
        'slopweb/runtime/FontProbe/bin/Debug/net10.0/FontProbe.runtimeconfig.json',
        'slopweb/runtime/BackgroundProbe/bin/Debug/net10.0/BackgroundProbe.dll',
        'slopweb/runtime/BackgroundProbe/bin/Debug/net10.0/BackgroundProbe.deps.json',
        'slopweb/runtime/BackgroundProbe/bin/Debug/net10.0/BackgroundProbe.runtimeconfig.json',
        'slopweb/runtime/core_pack.py',
        'slopweb/runtime/bin/Debug/net10.0/Probe.dll',
        'slopweb/runtime/Adapters/bin/Debug/net10.0/Adapters.dll',
        'slopweb/runtime/wwwroot/local-fonts.mjs',
        'slopweb/runtime/wwwroot/local-fonts.wasm',
        'slopweb/runtime/wwwroot/local-fonts.LICENSE.txt',
        'slopweb/runtime/bin/Debug/net10.0/wwwroot/_framework/dotnet.js',
        'slopweb/runtime/wwwroot/index.html',
        'slopweb/runtime/Scanner/bin/Debug/net10.0/Scanner.dll',
        'slopweb/offline/bin/Debug/net10.0/Offline.dll',
        'mod/Textures/SlopWorld/LoadingFont.png',
        'mod/Sounds/SlopWorld/OST/pace-20261012.ogg',
        'shared/loading-tips.txt',
    ]
    inputs += [
        f'mod/Assemblies/{name}.dll'
        for name in ('SlopWorld', '0Harmony', 'Google.Protobuf', 'Markdig', 'AngleSharp', 'Tomlyn')
    ]
    inputs += [f'slopweb/server/{name}.mjs' for name in ('server', 'bridge', 'websocket', 'fonts')]
    for name in inputs:
        path = root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text('redistributable fixture')
    shutil.copyfile(ROOT / 'slopweb/runtime/Probe.csproj', runtime / 'Probe.csproj')
    for name in slopweb.AOT_SOURCES:
        (runtime / name).write_text('authored C# fixture')
    for name in ('unlisted.cs', 'packages.lock.json', 'private.dll'):
        (runtime / name).write_text('must never ship')
    shutil.copyfile(ROOT / 'slopweb/runtime/mod_pack.py', runtime / 'mod_pack.py')
    shutil.copyfile(ROOT / 'mod/Textures/SlopWorld/LoadingFont.png', root / 'mod/Textures/SlopWorld/LoadingFont.png')
    (root / 'mod/Defs').mkdir()
    for name in ('KeyBindings.xml', 'PlannerBench.xml', 'Jukebox.xml'):
        (root / 'mod/Defs' / name).write_text('<Defs/>')
    monkeypatch.setattr(
        slopweb,
        'tracked_mod_files',
        lambda: {
            'Textures/SlopWorld/LoadingFont.png',
            'Defs/KeyBindings.xml',
            'Defs/PlannerBench.xml',
            'Defs/Jukebox.xml',
            'Sounds/SlopWorld/OST/pace-20261012.ogg',
        },
    )
    (root / 'mod/Sounds/SlopWorld/OST/private.ogg').write_text('must never ship')
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
        (
            'game/RimWorld.dll',
            'assets/Core.zip',
            'assets/Assets.zip',
            'assets/portraits/private.rgba',
            'credentials.toml',
        )
        if private_runtime
        else ()
    ):
        path = runtime / 'out/wwwroot-eco' / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text('must never ship')
    (runtime / 'FontProbe/bin/Debug/net10.0/Brutal.ImGui.dll').write_text('must never ship')
    monkeypatch.setattr(slopweb, 'ROOT', root)
    output = tmp_path / 'bundle'
    slopweb.build(output)
    assert (output / 'ost/pace-20261012.ogg').is_file()
    assert not (output / 'ost/private.ogg').exists()
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
    assert ('libfontconfig1' if distribution == 'debian' else 'fontconfig') in metadata[0]


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
    source = ROOT / 'slopweb/runtime'
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


def test_runtime_notices_follow_resolved_sdk_pack(tmp_path: Path) -> None:
    runtime = tmp_path / 'runtime'
    obj = runtime / 'obj/Debug/net10.0'
    obj.mkdir(parents=True)
    package = tmp_path / 'sdk/packs/Microsoft.NETCore.App.Runtime.Mono.browser-wasm/10.0.14'
    source = package / 'runtimes/browser-wasm/native/dotnet.native.wasm'
    (obj / 'staticwebassets.build.json').write_text(json.dumps({'Assets': [{'OriginalItemSpec': str(source)}]}))
    assert slopweb.runtime_package(runtime) == package


def test_runtime_notices_reject_ambiguous_resolved_packs(tmp_path: Path) -> None:
    obj = tmp_path / 'obj/Debug/net10.0'
    obj.mkdir(parents=True)
    assets = [
        {
            'OriginalItemSpec': str(
                tmp_path
                / 'packs/Microsoft.NETCore.App.Runtime.Mono.browser-wasm'
                / version
                / 'runtimes/browser-wasm/native/dotnet.native.wasm'
            )
        }
        for version in ('10.0.14', '10.0.15')
    ]
    (obj / 'staticwebassets.build.json').write_text(json.dumps({'Assets': assets}))
    with pytest.raises(ValueError, match='multiple'):
        slopweb.runtime_package(tmp_path)
