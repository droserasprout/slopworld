"""Guard package boundaries and install ownership."""

import json
import os
import runpy
import shutil
import subprocess
import sys
import time
import zipfile
from pathlib import Path
from typing import Any

import pytest

from tools import ROOT
from tools import cli_completions
from tools.release import slopweb


def test_distribution_host_override_preserves_source_bundle_and_rekeys_payload(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    bundle = tmp_path / 'bundle'
    bundle.mkdir()
    (bundle / 'BUILD_ID').write_text('original')
    (bundle / 'slopweb-host').write_bytes(b'host-libc')
    replacement = tmp_path / 'target-host'
    replacement.write_bytes(b'target-libc')
    monkeypatch.setenv('SLOPWEB_HOST_BINARY', str(replacement))
    payload = tmp_path / 'payload'
    slopweb.stage(payload, 'revision', '1.2.3', bundle)
    installed = payload / 'usr/share/slopweb'
    assert (installed / 'slopweb-host').read_bytes() == b'target-libc'
    assert (installed / 'slopweb-host').stat().st_mode & 0o777 == 0o755
    assert (installed / 'BUILD_ID').read_text() == slopweb.bundle_identity(installed)
    assert (bundle / 'slopweb-host').read_bytes() == b'host-libc'
    assert (bundle / 'BUILD_ID').read_text() == 'original'


@pytest.mark.parametrize('version', ['', '2.3.4'])
def test_build_identity_uses_shared_version_resolution_and_explicit_override(
    monkeypatch: pytest.MonkeyPatch, version: str
) -> None:
    monkeypatch.setenv('VERSION', version)
    monkeypatch.setattr(slopweb, 'package_version', lambda root: '1.2.3')

    def from_git(fallback: str, repository: Path) -> str:
        assert fallback == '1.2.3' and repository == ROOT
        return '1.2.3-development'

    monkeypatch.setattr(slopweb, 'from_git', from_git)
    monkeypatch.setattr(slopweb, 'run', lambda *args, **kwargs: subprocess.CompletedProcess([], 0, 'built-revision\n'))
    assert slopweb.build_identity() == ('built-revision', version or '1.2.3-development')


@pytest.mark.parametrize('private_runtime', [False, True])
def test_distributable_build_never_reads_staged_game_root(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, private_runtime: bool
) -> None:
    root = tmp_path / 'source'
    runtime = root / 'slopweb/runtime'
    inputs = [
        'slopweb/host/target/release/slopweb',
        'slopweb/runtime/FontProbe/bin/Debug/net10.0/FontProbe.dll',
        'slopweb/runtime/FontProbe/bin/Debug/net10.0/FontProbe.deps.json',
        'slopweb/runtime/FontProbe/bin/Debug/net10.0/FontProbe.runtimeconfig.json',
        'slopweb/runtime/BackgroundProbe/bin/Debug/net10.0/BackgroundProbe.dll',
        'slopweb/runtime/BackgroundProbe/bin/Debug/net10.0/BackgroundProbe.deps.json',
        'slopweb/runtime/BackgroundProbe/bin/Debug/net10.0/BackgroundProbe.runtimeconfig.json',
        'slopweb/runtime/bin/Debug/net10.0/Probe.dll',
        'slopweb/runtime/Adapters/bin/Debug/net10.0/Adapters.dll',
        'slopweb/runtime/wwwroot/local-fonts.mjs',
        'slopweb/runtime/wwwroot/local-fonts.wasm',
        'slopweb/runtime/wwwroot/local-fonts.LICENSE.txt',
        'slopweb/runtime/bin/Debug/net10.0/wwwroot/_framework/dotnet.js',
        'slopweb/runtime/wwwroot/index.html',
        'docs/theme/favicon.png',
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
            'assets/Eco.zip',
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
    (runtime / 'bin/Debug/net10.0/wwwroot/old-probe.js').write_text('must never ship')
    monkeypatch.setattr(slopweb, 'ROOT', root)
    monkeypatch.setattr(slopweb, 'build_identity', lambda: ('built-revision', '1.2.3-built'))
    output = tmp_path / 'bundle'
    slopweb.build(output)
    assert (output / 'web/favicon.png').read_bytes() == (root / 'docs/theme/favicon.png').read_bytes()
    assert (output / 'ost/pace-20261012.ogg').is_file()
    assert not (output / 'ost/private.ogg').exists()
    assert (output / 'BUILD_ID').is_file()
    assert {path.name for path in (output / 'aot-src').iterdir()} == {'Probe.csproj', *slopweb.AOT_SOURCES}
    assert (output / 'slopweb-host').is_file()
    assert not list(output.rglob('*.py'))
    assert not (output / 'server').exists()
    assert (output / 'VERSION').read_text() == '1.2.3-built\n'
    assert (output / 'REVISION').read_text() == 'built-revision\n'
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
    (bundle / 'slopweb-host').write_text('fixture')
    (bundle / 'VERSION').write_text('stale version')
    (bundle / 'REVISION').write_text('stale revision')
    monkeypatch.setenv('SOURCE_DATE_EPOCH', '1234567890')
    metadata = []

    def run(args: list[str], **kwargs: Any) -> subprocess.CompletedProcess[str]:
        if args[0] == 'dpkg-shlibdeps':
            assert args[-1].endswith('/usr/share/slopweb/slopweb-host')
            return subprocess.CompletedProcess(args, 0, 'shlibs:Depends=libc6 (>= 2.39)\n')
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
        assert (payload / 'usr/share/slopweb/slopweb-host').stat().st_mode & 0o777 == 0o755
        assert not (payload / 'usr/share/slopworld').exists()
        assert not (payload / 'usr/lib/systemd').exists()
        assert (payload / 'usr/share/slopweb/BUILD_ID').read_text() == 'fixture'
        for directory in ('usr/share/slopweb', 'usr/share/doc/slopweb'):
            assert (payload / directory / 'VERSION').read_text() == '1.2.3\n'
            assert (payload / directory / 'REVISION').read_text() == 'revision\n'
        for shell in cli_completions.SHELLS:
            assert (payload / 'usr/share' / cli_completions.relative_path(shell, 'slopweb')).is_file()
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
    assert 'nodejs' not in metadata[0] and 'python' not in metadata[0] and 'dotnet-sdk' in metadata[0]
    assert ('libfontconfig1' if distribution == 'debian' else 'fontconfig') in metadata[0]


def test_uninstall_preserves_unrelated_files_and_private_cache(tmp_path: Path) -> None:
    completions = [name for name in slopweb.OWNED if 'completions' in name or 'site-functions' in name]
    for name in ('bin/slopweb', 'bin/slopd', 'share/slopweb/launch.py', '.cache/slopweb/game.dll', *completions):
        path = tmp_path / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text('fixture')
    for _ in range(2):
        slopweb.install(tmp_path, uninstall=True)
    assert not (tmp_path / 'bin/slopweb').exists()
    assert not (tmp_path / 'share/slopweb').exists()
    assert (tmp_path / 'bin/slopd').is_file()
    assert (tmp_path / '.cache/slopweb/game.dll').is_file()
    assert all(not (tmp_path / name).exists() for name in completions)


@pytest.mark.parametrize('uninstall', [False, True])
@pytest.mark.parametrize(
    'link',
    [
        'ancestor',
        'prefix',
        *sorted({str(parent) for name in slopweb.OWNED for parent in Path(name).parents if str(parent) != '.'}),
    ],
)
def test_source_changes_reject_symlinked_ancestors_before_mutating_any_owner(
    tmp_path: Path, source_install: None, uninstall: bool, link: str
) -> None:
    ancestor = tmp_path / 'ancestor'
    prefix = ancestor / 'prefix'
    for relative in slopweb.OWNED:
        path = prefix / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text('installed')
    target = ancestor if link == 'ancestor' else prefix if link == 'prefix' else prefix / link
    outside = tmp_path / 'outside'
    target.rename(outside)
    target.symlink_to(outside, target_is_directory=True)
    expected = {path.relative_to(tmp_path): path.read_text() for path in tmp_path.rglob('*') if path.is_file()}

    for _ in range(2):
        with pytest.raises(ValueError, match='symlink'):
            slopweb.install(prefix, uninstall=uninstall)
        assert target.is_symlink()
        assert {
            path.relative_to(tmp_path): path.read_text() for path in tmp_path.rglob('*') if path.is_file()
        } == expected
        assert all((prefix / relative).read_text() == 'installed' for relative in slopweb.OWNED)
        assert not list(prefix.glob('.slopweb-install-*'))


@pytest.mark.parametrize('relative', slopweb.OWNED)
@pytest.mark.parametrize('kind', ['file', 'directory', 'dangling'])
def test_uninstall_unlinks_owned_leaf_symlinks_without_touching_targets(
    tmp_path: Path, relative: str, kind: str
) -> None:
    outside = tmp_path / 'outside'
    if kind == 'directory':
        outside.mkdir()
        (outside / 'private').write_text('private')
    elif kind == 'file':
        outside.write_text('private')
    prefix = tmp_path / 'prefix'
    leaf = prefix / relative
    leaf.parent.mkdir(parents=True)
    leaf.symlink_to(outside, target_is_directory=kind == 'directory')
    for _ in range(2):
        slopweb.install(prefix, uninstall=True)
        assert not leaf.is_symlink()
        assert not leaf.exists()
        if kind == 'directory':
            assert (outside / 'private').read_text() == 'private'
        elif kind == 'file':
            assert outside.read_text() == 'private'
        else:
            assert not outside.exists()


@pytest.mark.parametrize('command', ['install', 'uninstall'])
def test_source_cli_keeps_symlinked_prefix_visible_to_validation(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, command: str
) -> None:
    outside = tmp_path / 'outside'
    outside.mkdir()
    prefix = tmp_path / 'prefix'
    prefix.symlink_to(outside, target_is_directory=True)
    monkeypatch.setattr(sys, 'argv', ['slopweb', command, '--prefix', str(prefix)])
    with pytest.raises(ValueError, match='symlink'):
        slopweb.main()
    assert prefix.is_symlink()
    assert not list(outside.iterdir())


def test_installed_information_commands_need_no_checkout_or_game(tmp_path: Path) -> None:
    bundle = tmp_path / 'bundle'
    bundle.mkdir()
    (bundle / 'BUILD_ID').write_text('fixture')
    native = ROOT / 'slopweb/host/target/debug/slopweb'
    if not native.is_file():
        pytest.skip('Run just web-host-build to exercise the native installed launcher')
    shutil.copyfile(native, bundle / 'slopweb-host')
    (bundle / 'slopweb-host').chmod(0o755)
    payload = tmp_path / 'installed'
    slopweb.stage(payload, 'reviewed-revision', '1.2.3', bundle)
    launcher = payload / 'usr/bin/slopweb'
    home = tmp_path / 'home'
    home.mkdir()
    env = {
        'PATH': os.environ['PATH'],
        'HOME': str(home),
        'SLOPD_DATA': str(home / 'data'),
        'SLOPD_CONFIG_ROOT': str(home / 'config'),
        'PYTHONPATH': '',
    }
    for args, expected in (
        (['--version'], 'slopweb 1.2.3'),
        (['--endpoint', 'remote', 'paths', 'profile'], str(home / 'data/profiles/remote')),
        (['--sidecar', 'paths', 'endpoint'], str(home / 'config/sidecar/endpoints/local.toml')),
    ):
        result = subprocess.run([str(launcher), *args], cwd=home, env=env, check=True, capture_output=True, text=True)
        assert result.stdout.strip() == expected
    for shell in cli_completions.SHELLS:
        result = subprocess.run(
            [str(launcher), 'completions', shell], cwd=home, env=env, check=True, capture_output=True, text=True
        )
        packaged = payload / 'usr/share' / cli_completions.relative_path(shell, 'slopweb')
        assert result.stdout == packaged.read_text()
    assert not list(home.iterdir())


@pytest.fixture
def source_bundle(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> Path:
    bundle = tmp_path / 'bundle'
    bundle.mkdir()
    (bundle / 'VERSION').write_text('1.2.3-built\n')
    (bundle / 'REVISION').write_text('built-revision\n')
    monkeypatch.setattr(slopweb, 'OUTPUT', bundle)
    return bundle


@pytest.fixture
def source_install(monkeypatch: pytest.MonkeyPatch, source_bundle: Path) -> None:
    def stage(payload: Path, revision: str, version: str) -> None:
        assert (revision, version) == ('built-revision', '1.2.3-built')
        for name in (
            'bin/slopweb',
            'share/slopweb/current.py',
            'share/doc/slopweb/README.md',
            'share/licenses/slopweb/LICENSE',
            *(name for name in slopweb.OWNED if 'completions' in name or 'site-functions' in name),
        ):
            target = payload / 'usr' / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text('new')

    monkeypatch.setattr(slopweb, 'stage', stage)


def test_source_install_replaces_owned_trees_and_preserves_other_owners(tmp_path: Path, source_install: None) -> None:
    for relative in ('share/slopweb', 'share/doc/slopweb', 'share/licenses/slopweb'):
        path = tmp_path / relative / 'obsolete'
        path.parent.mkdir(parents=True)
        path.write_text('old')
    cache = tmp_path / '.cache/slopweb/private.dll'
    cache.parent.mkdir(parents=True)
    cache.write_text('private')
    (tmp_path / 'bin').mkdir()
    (tmp_path / 'bin/slopd').write_text('other owner')
    other_completions = [
        tmp_path / 'share' / cli_completions.relative_path(shell, 'slopd') for shell in cli_completions.SHELLS
    ]
    for path in other_completions:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text('other completion owner')
    for _ in range(2):
        slopweb.install(tmp_path)
        assert (tmp_path / 'share/slopweb/current.py').read_text() == 'new'
        assert not list(tmp_path.rglob('obsolete'))
        assert (tmp_path / 'bin/slopd').read_text() == 'other owner'
        assert cache.read_text() == 'private'
        assert all(path.read_text() == 'other completion owner' for path in other_completions)
        assert not list(tmp_path.glob('.slopweb-install-*'))


@pytest.mark.parametrize('existing', [False, True])
@pytest.mark.parametrize('failed_owner', slopweb.OWNED)
def test_source_install_rolls_back_failed_handoff(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, source_install: None, existing: bool, failed_owner: str
) -> None:
    owners = slopweb.OWNED
    directories = ('share/slopweb', 'share/doc/slopweb', 'share/licenses/slopweb')
    if existing:
        for relative in owners:
            target = tmp_path / relative
            if relative in directories:
                target = target / 'old'
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text('old')
    rename = Path.rename

    def fail_publish(path: Path, target: Path) -> Path:
        if '/usr/' in str(path) and target == tmp_path / failed_owner:
            raise OSError('handoff failed')
        return rename(path, target)

    monkeypatch.setattr(Path, 'rename', fail_publish)
    with pytest.raises(OSError, match='handoff failed'):
        slopweb.install(tmp_path)
    for relative in owners:
        target = tmp_path / relative
        if existing:
            if relative in directories:
                assert sorted(path.name for path in target.iterdir()) == ['old']
                target = target / 'old'
            assert target.read_text() == 'old'
        else:
            assert not target.exists()
    assert not list(tmp_path.glob('.slopweb-install-*'))


@pytest.mark.parametrize('link', ['share/slopweb', 'share/doc', 'share/licenses/slopweb'])
def test_source_install_rejects_symlinks_before_replacing_any_owner(
    tmp_path: Path, source_install: None, link: str
) -> None:
    prefix = tmp_path / 'prefix'
    launcher = prefix / 'bin/slopweb'
    launcher.parent.mkdir(parents=True)
    launcher.write_text('old launcher')
    outside = tmp_path / 'outside'
    outside.mkdir()
    (outside / 'private').write_text('private')
    target = prefix / link
    target.parent.mkdir(parents=True, exist_ok=True)
    target.symlink_to(outside, target_is_directory=True)
    with pytest.raises(ValueError, match='symlink'):
        slopweb.install(prefix)
    assert target.is_symlink()
    assert launcher.read_text() == 'old launcher'
    assert sorted(path.name for path in outside.iterdir()) == ['private']


def test_source_install_staging_failure_preserves_current_install(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, source_bundle: Path
) -> None:
    launcher = tmp_path / 'bin/slopweb'
    launcher.parent.mkdir()
    launcher.write_text('old launcher')

    def fail_stage(*args: Any) -> None:
        raise OSError('staging failed')

    monkeypatch.setattr(slopweb, 'stage', fail_stage)
    with pytest.raises(OSError, match='staging failed'):
        slopweb.install(tmp_path)
    assert launcher.read_text() == 'old launcher'
    assert not list(tmp_path.glob('.slopweb-install-*'))


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
        'Eco.zip',
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


def test_content_archive_ignores_clock_mtimes_and_modes(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    from PIL import Image

    root = tmp_path / 'mod'
    (root / 'Defs').mkdir(parents=True)
    (root / 'Textures/SlopWorld').mkdir(parents=True)
    (tmp_path / 'shared').mkdir()
    (tmp_path / 'shared/loading-tips.txt').write_text('tip')
    for name in ('KeyBindings', 'PlannerBench', 'Jukebox'):
        (root / 'Defs' / f'{name}.xml').write_text('<Defs/>')
    image = Image.new('RGBA', (1, 2))
    image.putdata([(1, 2, 3, 4), (5, 6, 7, 8)])
    image.save(root / 'Textures/SlopWorld/LoadingFont.png')
    output = tmp_path / 'Content.zip'
    monkeypatch.setattr(sys, 'argv', ['mod_pack.py', str(root), str(output)])

    def pack(year: int) -> bytes:
        monkeypatch.setattr(time, 'localtime', lambda *_: time.struct_time((year, 2, 3, 4, 5, 6, 0, 0, 0)))
        runpy.run_path(str(ROOT / 'slopweb/runtime/mod_pack.py'))
        return output.read_bytes()

    first = pack(2020)
    for path in root.rglob('*'):
        if path.is_file():
            os.utime(path, (1800000000, 1800000000))
            path.chmod(0o600)
    assert pack(2030) == first
    with zipfile.ZipFile(output) as archive:
        assert archive.read('Textures/SlopWorld/LoadingFont.rgba') == (
            b'\x01\x00\x00\x00\x02\x00\x00\x00' + bytes([5, 6, 7, 8, 1, 2, 3, 4])
        )
        assert all(entry.compress_type == zipfile.ZIP_DEFLATED for entry in archive.infolist())
    (root / 'Defs/Jukebox.xml').write_text('<Defs>changed</Defs>')
    assert pack(2030) != first
