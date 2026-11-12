"""Own mixed-AOT compiler inputs and private immutable runtime composition.

launch owns game preparation; this module consumes its final immutable DLLs.
Release staging ships only declared authored compiler inputs, never private output.
"""

import codecs
import fcntl
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import threading
import xml.etree.ElementTree as ET
from collections.abc import Iterator
from contextlib import contextmanager
from datetime import UTC
from datetime import datetime
from pathlib import Path
from typing import Any
from typing import TextIO

PACKAGES = {
    'Newtonsoft.Json': '13.0.3',
    'Tmds.DBus.Protocol': '0.95.1',
    'StbImageSharp': '2.27.13',
    'StbImageWriteSharp': '1.16.7',
}
INTERPRETED = ('Waylith', 'Waylith.Injection', 'RimWorldWaylith.PrepatcherCompat')


class MissingWorkload(ValueError):
    """A valid SDK needs matching workload installation before it can compile."""

    def __init__(self, sdk: Path, version: str, reason: str):
        self.sdk = sdk
        self.version = version
        super().__init__(f'AOT SDK {version} at {sdk} lacks {reason}; install it separately')


def published_loader(main: str) -> str:
    marker = '    const loaded = [];'
    if main.count(marker) != 1 or 'LoadPublishedAssembly' in main:
        raise ValueError('Unrecognized prepared main.js loading boundary')
    main = main.replace(
        marker,
        marker + '\n    const loadPublished = response => exports.Probe.LoadPublishedAssembly('
        "decodeURIComponent(new URL(response.url).pathname.split('/').pop()).replace(/\\.dll$/, ''));",
    )
    for variable in ('response', 'adapters'):
        main = main.replace(
            f'exports.Probe.LoadAssembly(new Uint8Array(await {variable}.arrayBuffer()))',
            f'loadPublished({variable})',
        )
    if 'Probe.LoadAssembly(' in main or 'loadPublished(response)' not in main:
        raise ValueError('Unrecognized stream assembly load; refusing mixed identities')
    return main


def assemblies(prepared: Path) -> dict[str, Path]:
    for required in ('main.js', 'game/manifest.json', 'game/Assembly-CSharp.dll', 'mod/SlopWorld.dll', 'Adapters.dll'):
        if not (prepared / required).is_file():
            raise ValueError(f'Missing final prepared input: {required}')
    # Reject aliases before copytree follows links, including links to host secrets.
    if any(path.is_symlink() for path in prepared.rglob('*')):
        raise ValueError('Prepared snapshots must not contain symlinks')
    dlls = {}
    for folder in (prepared / 'game', prepared / 'mod', prepared):
        for dll in sorted(folder.glob('*.dll')):
            if dll.stem in dlls:
                raise ValueError(f'Duplicate assembly name: {dll.stem}')
            dlls[dll.stem] = dll
    if set(dlls) & ({'Probe'} | set(PACKAGES)):
        raise ValueError('Prepared assemblies collide with Probe or pinned package references')
    if not set(INTERPRETED).issubset(dlls):
        raise ValueError('Prepared root is missing custom CLR owners')

    return dlls


def write_candidate(runtime: Path, candidate: Path, dlls: dict[str, Path], runtime_version: str | None = None) -> None:
    assemblies = candidate / 'assemblies'
    assemblies.mkdir(parents=True)
    # Cache placement must not inherit compiler policy from an arbitrary ancestor.
    (candidate / 'Directory.Build.props').write_text('<Project />\n')
    project = ET.parse(runtime / 'Probe.csproj')
    props = project.getroot().find('PropertyGroup')
    if props is None:
        raise ValueError('Runtime project has no compiler properties')
    for name, value in {
        'RunAOTCompilation': 'true',
        'PublishTrimmed': 'true',
        'BlazorWebAssemblyEnableLinking': 'true',
        'WasmStripILAfterAOT': 'false',
        'JsonSerializerIsReflectionEnabledByDefault': 'true',
        'RestorePackagesWithLockFile': 'false',
        'CompressionEnabled': 'false',
        'BlazorEnableCompression': 'false',
    }.items():
        item = props.find(name)
        if item is None:
            item = ET.SubElement(props, name)
        item.text = value
    if runtime_version:
        ET.SubElement(props, 'RuntimeFrameworkVersion').text = runtime_version
    sources = candidate / 'src'
    sources.mkdir()
    for compile_item in project.findall('.//Compile'):
        name = compile_item.get('Include', '')
        if not name:
            raise ValueError('Runtime compile input has no path')
        source = (runtime / name).read_text()
        (sources / name).write_text(source)
        compile_item.set('Include', f'src/{name}')
    group = ET.SubElement(project.getroot(), 'ItemGroup')
    for name, dll in sorted(dlls.items()):
        shutil.copy2(dll, assemblies / dll.name)
        reference = ET.SubElement(group, 'Reference', Include=name)
        ET.SubElement(reference, 'HintPath').text = f'assemblies/{dll.name}'
        ET.SubElement(reference, 'Private').text = 'true'
    for name, version in PACKAGES.items():
        ET.SubElement(group, 'PackageReference', Include=name, Version=version)
    for name in sorted(set(dlls) | set(PACKAGES) | {'Probe'}):
        ET.SubElement(group, 'TrimmerRootAssembly', Include=name)
    ET.indent(project)
    project.write(candidate / 'Probe.csproj', encoding='unicode')
    targets = ET.Element('Project')
    target = ET.SubElement(
        targets, 'Target', Name='KeepCustomClrOwnersInterpreted', AfterTargets='_GetDefaultWasmAssembliesToBundle'
    )
    items = ET.SubElement(target, 'ItemGroup')
    item = ET.SubElement(
        items,
        'WasmAssembliesToBundle',
        Update='@(WasmAssembliesToBundle)',
        Condition=' or '.join(f"'%(Filename)' == '{name}'" for name in INTERPRETED),
    )
    ET.SubElement(item, 'AOT_InternalForceToInterpret').text = 'true'
    ET.indent(targets)
    ET.ElementTree(targets).write(candidate / 'Directory.Build.targets', encoding='unicode')


def compiler_inputs(runtime: Path) -> list[Path]:
    """The authored project is the sole compile list; reject escapes and wildcards."""
    project = runtime / 'Probe.csproj'
    names = [item.get('Include', '') for item in ET.parse(project).findall('.//Compile')]
    if not names or len(names) != len(set(names)):
        raise ValueError('Probe must declare each compiler source exactly once')
    if any(Path(name).name != name or not name.endswith('.cs') or '*' in name for name in names):
        raise ValueError('Probe compiler sources must be explicit local C# filenames')
    return [project, *(runtime / name for name in names)]


def sdk_environment(sdk: Path) -> dict[str, str]:
    return {
        **os.environ,
        'DOTNET_ROOT': str(sdk.parent),
        'DOTNET_SKIP_FIRST_TIME_EXPERIENCE': '1',
        'DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE': 'true',
        'DOTNET_NOLOGO': '1',
    }


def sdk_selection(version: str) -> str:
    return json.dumps({'sdk': {'version': version, 'rollForward': 'disable', 'allowPrerelease': False}})


def toolchain(path: Path) -> tuple[Path, dict[str, Any]]:
    """Inspect an existing SDK in isolation from checkout/global.json selection."""
    sdk = path.expanduser().resolve(strict=True)
    if sdk.is_dir():
        sdk = (sdk / 'dotnet').resolve(strict=True)
    if sdk.name != 'dotnet' or not sdk.is_file() or not os.access(sdk, os.X_OK):
        raise ValueError('--aot-sdk requires an existing SDK directory or executable dotnet')
    with tempfile.TemporaryDirectory(prefix='slopweb-sdk-') as temporary:

        def query(*args: str) -> str:
            try:
                return subprocess.run(
                    [str(sdk), *args],
                    cwd=temporary,
                    env=sdk_environment(sdk),
                    check=True,
                    capture_output=True,
                    text=True,
                ).stdout.strip()
            except subprocess.CalledProcessError as error:
                raise ValueError(f'AOT SDK inspection failed: {sdk} {" ".join(args)}\n{error.stderr}') from error

        version = query('--version')
        if not re.fullmatch(r'10\.\d+\.\d+', version) or not (sdk.parent / 'sdk' / version).is_dir():
            raise ValueError(f'AOT requires an installed .NET 10 SDK; selected {version!r} at {sdk}')
        (Path(temporary) / 'global.json').write_text(sdk_selection(version))
        workloads = query('workload', 'list')
        if not re.search(r'^wasm-tools(?:\s|$)', workloads, re.MULTILINE):
            raise MissingWorkload(sdk, version, 'matching wasm-tools')
        bundled = sdk.parent / 'sdk' / version / 'Microsoft.NETCoreSdk.BundledVersions.props'
        runtime = next(
            (
                item.get('LatestRuntimeFrameworkVersion')
                for item in ET.parse(bundled).findall('.//KnownRuntimePack')
                if item.get('Include') == 'Microsoft.NETCore.App' and item.get('TargetFramework') == 'net10.0'
            ),
            None,
        )
        if not runtime or not re.fullmatch(r'10\.0\.\d+', runtime):
            raise ValueError(f'Cannot identify supported AOT target runtime in {bundled}')
        packs = sdk.parent / 'packs'
        if not packs.is_dir():
            raise MissingWorkload(sdk, version, 'wasm-tools compiler packs')
        wasm_sdk = any(
            (pack / runtime).is_dir()
            for pattern in ('Microsoft.NET.Runtime.WebAssembly.Sdk*', 'Microsoft.NET.Sdk.WebAssembly.Pack*')
            for pack in packs.glob(pattern)
        )
        cross = any(
            (pack / runtime).is_dir() for pack in packs.glob('Microsoft.NETCore.App.Runtime.AOT.*.Cross.browser-wasm')
        )
        if not wasm_sdk or not cross:
            raise MissingWorkload(sdk, version, f'wasm-tools compiler packs matching runtime {runtime}')
        # Only the selected SDK's WASM dependency graph affects compilation.
        # Unrelated desktop runtimes and workload installations are not inputs.
        selected_packs = {}
        for pack in packs.iterdir():
            if not (
                pack.name.startswith(
                    (
                        'Microsoft.NET.Runtime.WebAssembly.Sdk',
                        'Microsoft.NET.Sdk.WebAssembly.Pack',
                        'Microsoft.NET.Runtime.Emscripten.',
                    )
                )
                or pack.name.endswith('Cross.browser-wasm')
                or pack.name == 'Microsoft.NETCore.App.Runtime.Mono.browser-wasm'
            ):
                continue
            selected = pack / runtime
            if selected.is_dir():
                selected_packs[pack.name] = runtime
        identity = {
            'path': str(sdk),
            'version': version,
            'packs': selected_packs,
            'target_runtime': runtime,
            'sdk_properties': hashlib.sha256(bundled.read_bytes()).hexdigest(),
        }
    return sdk, identity


def digest_files(files: dict[str, Path], identity: object) -> str:
    digest = hashlib.sha256(json.dumps(identity, sort_keys=True).encode())
    for name, path in sorted(files.items()):
        digest.update(json.dumps([name, path.stat().st_size]).encode())
        with path.open('rb') as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b''):
                digest.update(chunk)
    return digest.hexdigest()


def digest_manifest(files: dict[str, Path], identity: object, contents: dict[Path, str]) -> str:
    """Hash content manifests; callers may share file digests within one snapshot.

    Never persist the memo: each preparation must read changed bytes even when
    their size and timestamp match the previous invocation.
    """
    digest = hashlib.sha256(json.dumps(['content-manifest-v1', identity], sort_keys=True).encode())
    for name, path in sorted(files.items()):
        if path not in contents:
            with path.open('rb') as stream:
                contents[path] = hashlib.file_digest(stream, 'sha256').hexdigest()
        digest.update(json.dumps([name, contents[path]]).encode())
    return digest.hexdigest()


@contextmanager
def build_lock(entry: Path) -> Iterator[TextIO]:
    # Never replace/unlink this inode: orphaned compilers retain it through pass_fds.
    with (entry / 'build.lock').open('a') as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            print(
                f'Waiting for another AOT build to release {entry / "build.lock"}\n'
                'A compiler can retain this lock after its launcher exits. '
                'Inspect the holder with lslocks; do not delete the lock file. Ctrl+C cancels this wait.',
                flush=True,
            )
            fcntl.flock(lock, fcntl.LOCK_EX)
            print(f'AOT build lock acquired; continuing: {entry}', flush=True)
        yield lock


def published_framework(entry: Path) -> Path:
    published = entry / 'published/wwwroot'
    framework = published / '_framework'
    if not (framework / 'dotnet.js').is_file() or not list(framework.glob('dotnet.native*.wasm')):
        raise ValueError(f'AOT publish has incomplete framework: {entry}')
    return published


@contextmanager
def stream_log(path: Path) -> Iterator[None]:
    """Mirror new log bytes while the compiler retains direct file/lock ownership."""
    stopped = threading.Event()
    with path.open('rb') as reader:
        reader.seek(0, os.SEEK_END)

        def forward() -> None:
            decoder = codecs.getincrementaldecoder('utf-8')(errors='replace')
            try:
                while True:
                    chunk = reader.read(8192)
                    if chunk:
                        sys.stderr.write(decoder.decode(chunk))
                        sys.stderr.flush()
                    elif stopped.is_set():
                        sys.stderr.write(decoder.decode(b'', final=True))
                        sys.stderr.flush()
                        return
                    else:
                        stopped.wait(0.1)
            except OSError:
                # A closed terminal must not interrupt compilation or its durable log.
                return

        worker = threading.Thread(target=forward, name='slopweb-publish-log', daemon=True)
        worker.start()
        try:
            yield
        finally:
            stopped.set()
            worker.join()


def compile_entry(entry: Path, sdk: Path, lock: TextIO, logs: Path | None = None) -> None:
    """Resume in the same project/obj tree, marking complete only after validation."""
    if (entry / 'publish.complete').is_file():
        published_framework(entry)
        return
    logs = logs or entry
    with (logs / 'publish.log').open('a') as log:
        log.write(f'\n--- Publish attempt {datetime.now(UTC).isoformat()} ---\n')
        log.flush()
        print(f'Publishing mixed AOT; log: {logs / "publish.log"}', flush=True)
        try:
            with stream_log(logs / 'publish.log'):
                subprocess.run(
                    [
                        str(sdk),
                        'publish',
                        str(entry / 'candidate/Probe.csproj'),
                        '-c',
                        'Release',
                        '-o',
                        str(entry / 'published'),
                        '--nologo',
                        '-v',
                        'minimal',
                        '-p:RestoreLockedMode=false',
                        f'-bl:{logs / "publish.binlog"}',
                    ],
                    cwd=entry / 'candidate',
                    env=sdk_environment(sdk),
                    stdout=log,
                    stderr=subprocess.STDOUT,
                    check=True,
                    pass_fds=(lock.fileno(),),
                )
        except subprocess.CalledProcessError as error:
            raise ValueError(f'AOT publish failed; retry to resume compilation. See {logs / "publish.log"}') from error
    published_framework(entry)
    (entry / 'publish.complete').touch()


def sync_candidate(source: Path, destination: Path) -> None:
    """Preserve timestamps for unchanged inputs; MSBuild owns bin/obj invalidation."""
    destination.mkdir(parents=True, exist_ok=True)
    for folder in ('src', 'assemblies'):
        target = destination / folder
        if target.exists():
            for path in target.rglob('*'):
                if path.is_file() and not (source / path.relative_to(destination)).exists():
                    path.unlink()
    for path in source.rglob('*'):
        if not path.is_file():
            continue
        target = destination / path.relative_to(source)
        target.parent.mkdir(parents=True, exist_ok=True)
        if not target.exists() or target.read_bytes() != path.read_bytes():
            # New timestamps are required even when a copied input has an old mtime.
            shutil.copyfile(path, target)


def prepare(bundle: Path, prepared: Path, cache: Path, sdk_path: Path) -> Path:
    """Cache native compilation separately from current private assets and loader."""
    sdk, sdk_identity = toolchain(sdk_path)
    dlls = assemblies(prepared)
    main = published_loader((prepared / 'main.js').read_text())
    compiler_inputs(bundle / 'aot-src')  # Validate the explicit source boundary before copying.
    # Hash actual generated inputs: orchestration/loader edits do not change code.
    cache.mkdir(parents=True, exist_ok=True, mode=0o700)
    with tempfile.TemporaryDirectory(prefix='.inputs-', dir=cache) as temporary:
        generated = Path(temporary) / 'candidate'
        write_candidate(bundle / 'aot-src', generated, dlls, sdk_identity['target_runtime'])
        (generated / 'global.json').write_text(sdk_selection(sdk_identity['version']))
        files = {str(path.relative_to(generated)): path for path in generated.rglob('*') if path.is_file()}
        key = digest_files(files, sdk_identity)
        # Keep workspace paths stable across source/assembly changes. Changes to
        # compiler policy or toolchain get an isolated intermediate directory.
        policy = {name: path for name, path in files.items() if name not in ('global.json',) and '/' not in name}
        workspace_key = digest_files(policy, sdk_identity)
        compilation = cache / 'aot-builds' / key
        compilation.mkdir(parents=True, exist_ok=True, mode=0o700)
        print(f'Checking mixed AOT cache: {compilation}', flush=True)
        with build_lock(compilation):
            if not (compilation / 'candidate').exists():
                generated.rename(compilation / 'candidate')
    # Hold through composition: publication and selection observe one success.
    with build_lock(compilation):
        if not (compilation / 'publish.complete').is_file():
            workspace = cache / 'aot-workspaces' / workspace_key
            workspace.mkdir(parents=True, exist_ok=True, mode=0o700)
            with build_lock(workspace) as lock:
                # Invalidate before touching inputs, including after cancellation.
                (workspace / 'publish.complete').unlink(missing_ok=True)
                sync_candidate(compilation / 'candidate', workspace / 'candidate')
                # Never let stale publish outputs survive a changed assembly set.
                if (workspace / 'published').exists():
                    shutil.rmtree(workspace / 'published')
                compile_entry(workspace, sdk, lock, compilation)
                with tempfile.TemporaryDirectory(prefix='.publish-', dir=compilation) as temporary:
                    result = Path(temporary) / 'published'
                    shutil.copytree(workspace / 'published', result)
                    if (compilation / 'published').exists():
                        shutil.rmtree(compilation / 'published')
                    result.rename(compilation / 'published')
                published_framework(compilation)
                (compilation / 'publish.complete').touch()
        published_framework(compilation)
        # Asset-only changes compose a fresh runtime without invoking dotnet publish.
        files = {str(path.relative_to(prepared)): path for path in prepared.rglob('*') if path.is_file()}
        runtime_key = digest_files(files, {'compilation': key, 'loader': main})
        runtimes = cache / 'aot-runtimes'
        runtimes.mkdir(exist_ok=True, mode=0o700)
        destination = runtimes / runtime_key
        if (destination / '.ready').is_file():
            return destination
        with tempfile.TemporaryDirectory(prefix='.compose-', dir=runtimes) as temporary:
            root = Path(temporary) / 'runtime'
            # The interpreter framework is replaced by the publication. Avoid
            # copying its large WASM payload only to immediately delete it.
            shutil.copytree(
                prepared,
                root,
                ignore=lambda directory, names: {'_framework'} if Path(directory) == prepared else set(),
            )
            shutil.copytree(published_framework(compilation), root, dirs_exist_ok=True)
            (root / 'main.js').write_text(main)
            (root / '.ready').write_text(runtime_key + '\n')
            root.rename(destination)
        return destination
