"""Game-free compiler cache, publication ordering, and launcher selection checks."""

import argparse
import concurrent.futures
import fcntl
import importlib
import json
import os
import shutil
import subprocess
import sys
import threading
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any
from typing import cast

import pytest

from tools import ROOT


def test_preparation_manifest_reads_shared_inputs_once_but_rechecks_next_invocation(
    aot: Any,
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    source = tmp_path / 'game.dll'
    source.write_bytes(b'first')
    original = source.stat()
    opened = []
    open_file = Path.open

    def observe(path: Path, *args: Any, **kwargs: Any) -> Any:
        if path == source:
            opened.append(path)
        return open_file(path, *args, **kwargs)

    monkeypatch.setattr(Path, 'open', observe)
    contents: dict[Path, str] = {}
    first = aot.digest_manifest({'game.dll': source}, 'phase', contents)
    assert aot.digest_manifest({'game.dll': source}, 'phase', contents) == first
    assert aot.digest_manifest({'renamed.dll': source}, 'phase', contents) != first
    assert aot.digest_manifest({'game.dll': source}, 'other-phase', contents) != first
    assert len(opened) == 1
    source.write_bytes(b'other')
    os.utime(source, ns=(original.st_atime_ns, original.st_mtime_ns))
    opened.clear()
    assert aot.digest_manifest({'game.dll': source}, 'phase', {}) != first
    assert len(opened) == 1


@pytest.fixture
def aot(monkeypatch: pytest.MonkeyPatch) -> Any:
    monkeypatch.syspath_prepend(str(ROOT / 'slopweb'))
    return importlib.import_module('aot')


@pytest.fixture
def inputs(aot: Any, tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> argparse.Namespace:
    bundle, prepared, cache = (tmp_path / name for name in ('bundle', 'prepared', 'cache'))
    sources = bundle / 'aot-src'
    sources.mkdir(parents=True)
    runtime = ROOT / 'experiments/browser/runtime'
    for path in aot.compiler_inputs(runtime):
        shutil.copyfile(path, sources / path.name)
    prepared.mkdir()
    shutil.copyfile(runtime / 'wwwroot/main.js', prepared / 'main.js')
    for name in (
        'game/manifest.json',
        'game/Assembly-CSharp.dll',
        'mod/SlopWorld.dll',
        'Adapters.dll',
        *(f'game/{owner}.dll' for owner in aot.INTERPRETED),
        '_framework/dotnet.js',
        'assets/fonts/private.ttf',
        'assets/appearance.json',
    ):
        path = prepared / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(name)
    sdk = tmp_path / 'sdk/dotnet'
    identity = {'path': str(sdk), 'version': '10.0.114', 'target_runtime': '10.0.14', 'workload': '10.0.14'}
    monkeypatch.setattr(aot, 'toolchain', lambda path: (sdk, identity.copy()))
    calls = []

    def publish(args: list[str], **kwargs: Any) -> subprocess.CompletedProcess[str]:
        calls.append((args, kwargs))
        assert args[0] == str(sdk)
        candidate = Path(kwargs['cwd'])
        assert json.loads((candidate / 'global.json').read_text())['sdk']['version'] == identity['version']
        assert (candidate / 'Directory.Build.props').read_text() == '<Project />\n'
        assert kwargs['pass_fds']
        framework = Path(args[args.index('-o') + 1]) / 'wwwroot/_framework'
        framework.mkdir(parents=True, exist_ok=True)
        (framework / 'dotnet.js').write_text('published framework')
        (framework / 'dotnet.native.wasm').write_text('published native wasm')
        return subprocess.CompletedProcess(args, 0)

    monkeypatch.setattr(aot.subprocess, 'run', publish)
    return argparse.Namespace(
        bundle=bundle,
        prepared=prepared,
        cache=cache,
        sdk=sdk,
        identity=identity,
        calls=calls,
        publish=publish,
        run=lambda: aot.prepare(bundle, prepared, cache, sdk),
    )


def test_compilation_hit_and_asset_only_changes_reuse_publish(aot: Any, inputs: Any) -> None:
    (inputs.prepared / '_framework/interpreter-only.wasm').write_bytes(b'unused interpreter')
    nested = inputs.prepared / 'assets/_framework/asset.txt'
    nested.parent.mkdir()
    nested.write_text('ordinary asset')
    root = inputs.run()
    assert inputs.run() == root
    assert len(inputs.calls) == 1
    assert (root / '_framework/dotnet.js').read_text() == 'published framework'
    assert not (root / '_framework/interpreter-only.wasm').exists()
    assert (root / 'assets/_framework/asset.txt').read_text() == 'ordinary asset'
    assert 'Probe.LoadAssembly(' not in (root / 'main.js').read_text()
    assert 'LoadPublishedAssembly' in (root / 'main.js').read_text()
    assert 'const bypassLogger = false;' in (root / 'main.js').read_text()
    for name in ('assets/fonts/private.ttf', 'assets/appearance.json', 'main.js'):
        with (inputs.prepared / name).open('a') as stream:
            stream.write('\n// updated asset')
        updated = inputs.run()
        assert updated != root
        assert (root / '.ready').is_file()
        assert len(inputs.calls) == 1
        assert (updated / name).read_text().endswith('// updated asset')
    project = ET.parse(next(inputs.cache.glob('aot-builds/*/candidate/Probe.csproj')))
    assert project.findtext('.//RuntimeFrameworkVersion') == '10.0.14'
    assert len(list(inputs.cache.glob('aot-builds/*/publish.complete'))) == 1


@pytest.mark.parametrize('change', ['dll', 'source', 'project', 'sdk', 'runtime', 'workload'])
def test_compiler_identity_invalidates_only_on_compiler_inputs(inputs: Any, change: str) -> None:
    root = inputs.run()
    paths = {
        'dll': inputs.prepared / 'mod/SlopWorld.dll',
        'source': inputs.bundle / 'aot-src/BrowserInput.cs',
        'project': inputs.bundle / 'aot-src/Probe.csproj',
    }
    if change == 'project':
        path = paths[change]
        path.write_text(path.read_text().replace('<Nullable>enable</Nullable>', '<Nullable>disable</Nullable>'))
    elif change in paths:
        with paths[change].open('a') as stream:
            stream.write('\n')
    else:
        field = {'sdk': 'version', 'runtime': 'target_runtime', 'workload': 'workload'}[change]
        inputs.identity[field] += '-changed'
    updated = inputs.run()
    assert updated != root
    assert len(inputs.calls) == 2
    assert (root / '.ready').is_file()


@pytest.mark.parametrize('failure', ['failed', 'interrupted', 'incomplete'])
def test_failed_publish_resumes_stable_candidate_and_never_selects_partial(
    aot: Any,
    inputs: Any,
    monkeypatch: pytest.MonkeyPatch,
    failure: str,
) -> None:
    attempts = []

    def fail(args: list[str], **kwargs: Any) -> None:
        candidate = Path(kwargs['cwd'])
        (candidate / 'cached.o').write_text('expensive compiled object')
        attempts.append(candidate)
        if failure == 'failed':
            raise subprocess.CalledProcessError(1, args)
        if failure == 'interrupted':
            raise KeyboardInterrupt

    monkeypatch.setattr(aot.subprocess, 'run', fail)
    with pytest.raises((ValueError, KeyboardInterrupt)):
        inputs.run()
    assert not list(inputs.cache.glob('aot-runtimes/*/.ready'))
    assert not list(inputs.cache.glob('aot-builds/*/publish.complete'))
    entry = next(inputs.cache.glob('aot-workspaces/*'))
    inode = (entry / 'build.lock').stat().st_ino
    monkeypatch.setattr(aot.subprocess, 'run', inputs.publish)
    root = inputs.run()
    assert (root / '.ready').is_file()
    assert attempts == [entry / 'candidate']
    assert (entry / 'candidate/cached.o').read_text() == 'expensive compiled object'
    assert (entry / 'build.lock').stat().st_ino == inode
    assert next(inputs.cache.glob('aot-builds/*/publish.log')).read_text().count('Publish attempt') == 2


def test_concurrent_publish_and_selection_share_one_success(
    aot: Any,
    inputs: Any,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    entered, release, second_entered = threading.Event(), threading.Event(), threading.Event()
    real_lock = aot.build_lock

    def observed_lock(entry: Path) -> Any:
        if entered.is_set():
            second_entered.set()
        return real_lock(entry)

    def held_publish(*args: Any, **kwargs: Any) -> Any:
        entered.set()
        assert release.wait(10)
        return inputs.publish(*args, **kwargs)

    monkeypatch.setattr(aot, 'build_lock', observed_lock)
    monkeypatch.setattr(aot.subprocess, 'run', held_publish)
    with concurrent.futures.ThreadPoolExecutor(2) as executor:
        first = executor.submit(inputs.run)
        assert entered.wait(10)
        second = executor.submit(inputs.run)
        try:
            assert second_entered.wait(10)
            assert not second.done()
            assert not list(inputs.cache.glob('aot-runtimes/*/.ready'))
        finally:
            release.set()
        assert first.result(timeout=10) == second.result(timeout=10)
    assert len(inputs.calls) == 1


def test_runtime_composition_holds_build_lock_and_recovers_from_copy_failure(
    aot: Any,
    inputs: Any,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    copytree = aot.shutil.copytree
    observed = []

    def failed_copy(source: Path, destination: Path, *args: Any, **kwargs: Any) -> None:
        if source == inputs.prepared:
            entry = next(inputs.cache.glob('aot-builds/*'))
            with (entry / 'build.lock').open('a') as contender, pytest.raises(BlockingIOError):
                fcntl.flock(contender, fcntl.LOCK_EX | fcntl.LOCK_NB)
            observed.append(True)
            raise OSError('composition failed')
        copytree(source, destination, *args, **kwargs)

    monkeypatch.setattr(aot.shutil, 'copytree', failed_copy)
    with pytest.raises(OSError, match='composition failed'):
        inputs.run()
    assert observed
    assert not list(inputs.cache.glob('aot-runtimes/*/.ready'))
    monkeypatch.setattr(aot.shutil, 'copytree', copytree)
    assert (inputs.run() / '.ready').is_file()
    assert len(inputs.calls) == 1


def test_compiler_inherits_lock_until_it_exits(aot: Any, tmp_path: Path) -> None:
    # A real harmless child proves the flock remains owned after Python closes its fd.
    with aot.build_lock(tmp_path) as lock:
        child = subprocess.Popen(
            [sys.executable, '-c', 'import sys; sys.stdin.read()'], stdin=subprocess.PIPE, pass_fds=(lock.fileno(),)
        )
    try:
        with (tmp_path / 'build.lock').open('a') as contender, pytest.raises(BlockingIOError):
            fcntl.flock(contender, fcntl.LOCK_EX | fcntl.LOCK_NB)
    finally:
        child.communicate(timeout=10)
    with aot.build_lock(tmp_path):
        pass


@pytest.mark.parametrize('prepare_only', [False, True])
@pytest.mark.parametrize('mode', ['default', 'explicit', 'environment', 'interpreted', 'missing'])
def test_launcher_selects_aot_by_default(
    aot: Any,
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    mode: str,
    prepare_only: bool,
) -> None:
    launch = importlib.import_module('launch')
    calls = []
    interpreted, native = tmp_path / 'interpreted', tmp_path / 'mixed'
    monkeypatch.setattr(launch, 'prepare', lambda *args: interpreted)
    monkeypatch.setattr(launch, 'environment', lambda args: {})
    monkeypatch.setattr(launch.os, 'umask', lambda value: None)
    monkeypatch.setattr(launch.shutil, 'which', lambda name: None if mode == 'missing' else '/path/dotnet')
    monkeypatch.setenv('SLOPWEB_AOT_SDK', '/environment/dotnet')
    if mode in ('default', 'missing'):
        monkeypatch.delenv('SLOPWEB_AOT_SDK')

    def select(*args: Any) -> Path:
        calls.append(args)
        return native

    monkeypatch.setattr(launch.aot, 'prepare', select)
    served = []
    monkeypatch.setattr(launch.os, 'execvpe', lambda *args: served.append(args))
    arguments = ['slopweb'] + (['--prepare'] if prepare_only else [])
    if mode == 'explicit':
        arguments += ['--aot-sdk', str(tmp_path / 'sdk')]
    elif mode == 'interpreted':
        arguments += ['--interpreted']
    monkeypatch.setattr(sys, 'argv', arguments)
    if mode == 'missing':
        with pytest.raises(SystemExit, match='1'):
            launch.main()
        assert not served and not calls
        assert '--aot-sdk' in capsys.readouterr().err
        return
    launch.main()
    selected = interpreted if mode == 'interpreted' else native
    if mode == 'interpreted':
        assert not calls
    else:
        assert calls[0][1] == interpreted
        expected_sdk = (
            tmp_path / 'sdk'
            if mode == 'explicit'
            else Path('/environment/dotnet' if mode == 'environment' else '/path/dotnet')
        )
        assert calls[0][3] == expected_sdk
    if prepare_only:
        assert str(selected) in capsys.readouterr().out
    else:
        assert str(selected) in served[0][1]


@pytest.fixture
def fake_sdk(tmp_path: Path, monkeypatch: pytest.MonkeyPatch, aot: Any) -> argparse.Namespace:
    sdk = tmp_path / 'sdk/dotnet'
    sdk.parent.mkdir()
    sdk.write_text('host fixture')
    sdk.chmod(0o700)
    selected = sdk.parent / 'sdk/10.0.114'
    selected.mkdir(parents=True)
    (selected / 'Microsoft.NETCoreSdk.BundledVersions.props').write_text(
        '<Project><ItemGroup><KnownRuntimePack Include="Microsoft.NETCore.App" TargetFramework="net10.0" '
        'LatestRuntimeFrameworkVersion="10.0.14" /></ItemGroup></Project>'
    )
    packs = sdk.parent / 'packs'
    for name in (
        'Microsoft.NET.Runtime.WebAssembly.Sdk.net10',
        'Microsoft.NETCore.App.Runtime.AOT.linux-x64.Cross.browser-wasm',
    ):
        (packs / name / '10.0.14').mkdir(parents=True)
    answers = {
        '--version': '10.0.114',
        'workload': 'wasm-tools    10.0.114/10.0.100',
        '--list-runtimes': 'Microsoft.NETCore.App 10.0.14',
    }
    calls = []

    def query(args: list[str], **kwargs: Any) -> subprocess.CompletedProcess[str]:
        calls.append((args, kwargs))
        assert Path(kwargs['cwd']) != ROOT
        assert kwargs['env']['DOTNET_ROOT'] == str(sdk.parent)
        selection = Path(kwargs['cwd']) / 'global.json'
        if args[1] == '--version':
            assert not selection.exists()
        else:
            assert json.loads(selection.read_text())['sdk'] == {
                'version': '10.0.114',
                'rollForward': 'disable',
                'allowPrerelease': False,
            }
        return subprocess.CompletedProcess(args, 0, answers[args[1]])

    monkeypatch.setattr(aot.subprocess, 'run', query)
    return argparse.Namespace(sdk=sdk, answers=answers, calls=calls)


def test_toolchain_uses_controlled_cwd_and_records_sdk_runtime_and_packs(aot: Any, fake_sdk: Any) -> None:
    sdk, identity = aot.toolchain(fake_sdk.sdk.parent)
    assert sdk == fake_sdk.sdk
    assert identity['version'] == '10.0.114'
    assert identity['target_runtime'] == '10.0.14'
    assert identity['sdk_properties']
    assert len(identity['packs']) == 2
    assert len(fake_sdk.calls) == 2


@pytest.mark.parametrize('failure', ['workload', 'mismatched_pack'])
@pytest.mark.parametrize('protected', [False, True])
def test_just_installs_pinned_workload_and_revalidates(
    aot: Any, fake_sdk: Any, monkeypatch: pytest.MonkeyPatch, failure: str, protected: bool
) -> None:
    launcher = importlib.import_module('just_launch')
    if failure == 'workload':
        fake_sdk.answers['workload'] = ''
    else:
        pack = next((fake_sdk.sdk.parent / 'packs').glob('Microsoft.NET.Runtime.WebAssembly.Sdk*/10.0.14'))
        pack.rename(pack.with_name('10.0.15'))
    original = aot.subprocess.run
    installs = []
    monkeypatch.setattr(launcher.os, 'geteuid', lambda: 1000)
    access = launcher.os.access
    monkeypatch.setattr(launcher.os, 'access', lambda path, mode: not protected if mode == 2 else access(path, mode))

    def run(command: list[str], **kwargs: Any) -> subprocess.CompletedProcess[str]:
        if 'install' not in command:
            return cast('subprocess.CompletedProcess[str]', original(command, **kwargs))
        installs.append(command)
        assert command == [
            *(['sudo'] if protected else []),
            str(fake_sdk.sdk),
            'workload',
            'install',
            'wasm-tools',
            '--version',
            '10.0.114',
        ]
        assert json.loads((Path(kwargs['cwd']) / 'global.json').read_text())['sdk']['version'] == '10.0.114'
        fake_sdk.answers['workload'] = 'wasm-tools 10.0.114/10.0.100'
        if failure == 'mismatched_pack':
            pack.mkdir()
        return subprocess.CompletedProcess(command, 0)

    monkeypatch.setattr(launcher.subprocess, 'run', run)
    launcher.ensure_workload(['--aot-sdk', str(fake_sdk.sdk)])
    assert len(installs) == 1
    launcher.ensure_workload(['--aot-sdk', str(fake_sdk.sdk)])
    assert len(installs) == 1


@pytest.mark.parametrize('arguments', [['--interpreted'], ['--help'], ['-h']])
def test_just_skips_toolchain_for_interpreter_and_help(
    aot: Any, monkeypatch: pytest.MonkeyPatch, arguments: list[str]
) -> None:
    launcher = importlib.import_module('just_launch')
    monkeypatch.setattr(aot, 'toolchain', lambda _: pytest.fail('unexpected SDK inspection'))
    launcher.ensure_workload(arguments)


@pytest.mark.parametrize('install_fails', [False, True])
def test_just_does_not_continue_after_failed_or_incomplete_install(
    aot: Any, fake_sdk: Any, monkeypatch: pytest.MonkeyPatch, install_fails: bool
) -> None:
    launcher = importlib.import_module('just_launch')
    fake_sdk.answers['workload'] = ''
    original = aot.subprocess.run

    def run(command: list[str], **kwargs: Any) -> subprocess.CompletedProcess[str]:
        if 'install' not in command:
            return cast('subprocess.CompletedProcess[str]', original(command, **kwargs))
        if install_fails:
            raise subprocess.CalledProcessError(1, command)
        return subprocess.CompletedProcess(command, 0)

    monkeypatch.setattr(launcher.subprocess, 'run', run)
    with pytest.raises(subprocess.CalledProcessError if install_fails else aot.MissingWorkload):
        launcher.ensure_workload(['--aot-sdk', str(fake_sdk.sdk)])


@pytest.mark.parametrize('failure', ['major', 'workload', 'mismatched_pack', 'not_executable'])
def test_toolchain_reports_missing_or_unsupported_sdk_clearly(aot: Any, fake_sdk: Any, failure: str) -> None:
    if failure == 'major':
        fake_sdk.answers['--version'] = '11.0.100'
    elif failure == 'workload':
        fake_sdk.answers['workload'] = ''
    elif failure == 'mismatched_pack':
        path = next((fake_sdk.sdk.parent / 'packs').glob('Microsoft.NET.Runtime.WebAssembly.Sdk*/10.0.14'))
        path.rename(path.with_name('10.0.13'))
    else:
        fake_sdk.sdk.chmod(0o600)
    with pytest.raises(ValueError, match=r'AOT|aot-sdk'):
        aot.toolchain(fake_sdk.sdk)


def test_failed_refresh_keeps_previous_serving_root(aot: Any, inputs: Any, monkeypatch: pytest.MonkeyPatch) -> None:
    previous = inputs.run()
    old_loader = (previous / 'main.js').read_bytes()
    (inputs.prepared / 'mod/SlopWorld.dll').write_text('changed managed assembly')

    def fail(args: list[str], **kwargs: Any) -> None:
        raise subprocess.CalledProcessError(1, args)

    monkeypatch.setattr(aot.subprocess, 'run', fail)
    with pytest.raises(ValueError, match='AOT publish failed'):
        inputs.run()
    assert (previous / '.ready').is_file()
    assert (previous / 'main.js').read_bytes() == old_loader
    assert list(inputs.cache.glob('aot-runtimes/*/.ready')) == [previous / '.ready']


def test_source_and_dll_edits_reuse_workspace_without_touching_unchanged_inputs(inputs: Any) -> None:
    original = inputs.run()
    workspace = next(inputs.cache.glob('aot-workspaces/*'))
    unchanged = workspace / 'candidate/assemblies/Assembly-CSharp.dll'
    timestamp = unchanged.stat().st_mtime_ns
    intermediate = workspace / 'candidate/obj/cached.o'
    intermediate.parent.mkdir()
    intermediate.write_text('compiler-owned intermediate')
    for path in (inputs.bundle / 'aot-src/BrowserInput.cs', inputs.prepared / 'mod/SlopWorld.dll'):
        path.write_text(path.read_text() + '\n// edit')
        assert inputs.run() != original
        assert unchanged.stat().st_mtime_ns == timestamp
        assert intermediate.read_text() == 'compiler-owned intermediate'
    assert len(list(inputs.cache.glob('aot-workspaces/*'))) == 1
    assert len(inputs.calls) == 3
    assert (original / '.ready').is_file()


def test_generated_policy_changes_isolate_workspace_and_project_whitespace_does_not(inputs: Any) -> None:
    original = inputs.run()
    project = inputs.bundle / 'aot-src/Probe.csproj'
    project.write_text(project.read_text() + '\n')
    assert inputs.run() == original
    assert len(inputs.calls) == 1
    project.write_text(project.read_text().replace('<Nullable>enable</Nullable>', '<Nullable>disable</Nullable>'))
    assert inputs.run() != original
    assert len(list(inputs.cache.glob('aot-workspaces/*'))) == 2


def test_sync_candidate_removes_deleted_inputs_but_keeps_compiler_state(aot: Any, tmp_path: Path) -> None:
    source, destination = tmp_path / 'source', tmp_path / 'destination'
    for root in (source, destination):
        (root / 'assemblies').mkdir(parents=True)
    (destination / 'assemblies/Removed.dll').write_text('obsolete')
    (destination / 'obj').mkdir()
    (destination / 'obj/state').write_text('owned by MSBuild')
    aot.sync_candidate(source, destination)
    assert not (destination / 'assemblies/Removed.dll').exists()
    assert (destination / 'obj/state').is_file()


def test_unrelated_sdk_installations_do_not_invalidate_toolchain(aot: Any, fake_sdk: Any) -> None:
    _, original = aot.toolchain(fake_sdk.sdk)
    (fake_sdk.sdk.parent / 'packs/Microsoft.Android.Sdk/99.0').mkdir(parents=True)
    (fake_sdk.sdk.parent / 'packs/Microsoft.NET.Runtime.WebAssembly.Wasi.Sdk/10.0.14').mkdir(parents=True)
    (fake_sdk.sdk.parent / 'packs/Microsoft.NET.Runtime.WebAssembly.Templates/10.0.14').mkdir(parents=True)
    fake_sdk.answers['workload'] += '\nandroid 99.0'
    fake_sdk.answers['--list-runtimes'] += '\nMicrosoft.NETCore.App 11.0.1'
    assert aot.toolchain(fake_sdk.sdk)[1] == original


def test_different_revisions_serialize_shared_workspace_until_publication(
    aot: Any, inputs: Any, monkeypatch: pytest.MonkeyPatch
) -> None:
    second_prepared = inputs.prepared.with_name('second-prepared')
    shutil.copytree(inputs.prepared, second_prepared)
    (second_prepared / 'mod/SlopWorld.dll').write_text('second revision')
    entered, contender, release = threading.Event(), threading.Event(), threading.Event()
    real_lock = aot.build_lock

    def observed_lock(entry: Path) -> Any:
        if entry.parent.name == 'aot-workspaces' and entered.is_set():
            contender.set()
        return real_lock(entry)

    def publish(args: list[str], **kwargs: Any) -> Any:
        assembly = Path(kwargs['cwd']) / 'assemblies/SlopWorld.dll'
        before = assembly.read_text()
        if not entered.is_set():
            entered.set()
            assert release.wait(10)
        assert assembly.read_text() == before
        return inputs.publish(args, **kwargs)

    monkeypatch.setattr(aot, 'build_lock', observed_lock)
    monkeypatch.setattr(aot.subprocess, 'run', publish)
    with concurrent.futures.ThreadPoolExecutor(2) as executor:
        first = executor.submit(inputs.run)
        assert entered.wait(10)
        second = executor.submit(aot.prepare, inputs.bundle, second_prepared, inputs.cache, inputs.sdk)
        try:
            assert contender.wait(10)
            assert not second.done()
        finally:
            release.set()
        roots = first.result(timeout=10), second.result(timeout=10)
    assert roots[0] != roots[1]
    assert (roots[0] / 'mod/SlopWorld.dll').read_text() == 'mod/SlopWorld.dll'
    assert (roots[1] / 'mod/SlopWorld.dll').read_text() == 'second revision'
    assert len(list(inputs.cache.glob('aot-workspaces/*'))) == 1
    assert len(inputs.calls) == 2
