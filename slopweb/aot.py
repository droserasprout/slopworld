"""Own mixed-AOT compiler inputs and private immutable runtime composition.

launch owns game preparation; this module consumes its final immutable DLLs.
Release staging ships only declared authored compiler inputs, never private output.
"""

import fcntl
import hashlib
import json
import os
import re
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET
from contextlib import contextmanager
from datetime import UTC
from datetime import datetime
from pathlib import Path

PACKAGES = {
    'Newtonsoft.Json': '13.0.3',
    'Tmds.DBus.Protocol': '0.95.1',
    'StbImageSharp': '2.27.13',
    'StbImageWriteSharp': '1.16.7',
}
INTERPRETED = ('Waylith', 'Waylith.Injection', 'RimWorldWaylith.PrepatcherCompat')


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
    # A published assembly has one body. Alternate skip-logger IL is not this snapshot.
    return main.replace(
        "const bypassLogger = parameters.has('skipLogger') && item.file === 'RimWorld.dll';",
        'const bypassLogger = false; // Published final IL only.',
    )


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
        name = compile_item.get('Include')
        source = (runtime / name).read_text()
        if name == 'Program.cs':
            marker = '    // Geometry stays coupled'
            if source.count(marker) != 1 or 'LoadPublishedAssembly' in source:
                raise ValueError('Unrecognized Probe export boundary')
            source = source.replace(
                marker,
                '    [JSExport]\n'
                '    public static string LoadPublishedAssembly(string name) => Assembly.Load(name).FullName!;\n\n'
                + marker,
            )
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


def toolchain(path: Path) -> tuple[Path, dict]:
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
            raise ValueError(f'AOT SDK {version} at {sdk} lacks matching wasm-tools; install it separately')
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
            raise ValueError(f'AOT SDK {version} at {sdk} lacks wasm-tools compiler packs; install them separately')
        wasm_packs = sorted(
            str(directory.relative_to(packs))
            for pack in packs.iterdir()
            if pack.is_dir()
            for directory in pack.iterdir()
            if directory.is_dir()
        )
        wasm_sdk = any(
            (pack / runtime).is_dir()
            for pattern in ('Microsoft.NET.Runtime.WebAssembly.Sdk*', 'Microsoft.NET.Sdk.WebAssembly.Pack*')
            for pack in packs.glob(pattern)
        )
        cross = any(
            (pack / runtime).is_dir() for pack in packs.glob('Microsoft.NETCore.App.Runtime.AOT.*.Cross.browser-wasm')
        )
        if not wasm_sdk or not cross:
            raise ValueError(
                f'AOT SDK {version} at {sdk} lacks wasm-tools compiler packs matching runtime {runtime}; install them separately'
            )
        manifests = {
            str(item.relative_to(sdk.parent)): hashlib.sha256(item.read_bytes()).hexdigest()
            for item in (sdk.parent / 'sdk-manifests').glob('**/WorkloadManifest.json')
        }
        identity = {
            'path': str(sdk),
            'version': version,
            'runtimes': query('--list-runtimes'),
            'workloads': workloads,
            'packs': wasm_packs,
            'target_runtime': runtime,
            'sdk_properties': hashlib.sha256(bundled.read_bytes()).hexdigest(),
            'manifests': manifests,
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


@contextmanager
def build_lock(entry: Path):
    # Never replace/unlink this inode: orphaned compilers retain it through pass_fds.
    with (entry / 'build.lock').open('a') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        yield lock


def published_framework(entry: Path) -> Path:
    published = entry / 'published/wwwroot'
    framework = published / '_framework'
    if not (framework / 'dotnet.js').is_file() or not list(framework.glob('dotnet.native*.wasm')):
        raise ValueError(f'AOT publish has incomplete framework: see {entry / "publish.log"}')
    return published


def compile_entry(entry: Path, sdk: Path, lock) -> None:
    """Resume in the same project/obj tree, marking complete only after validation."""
    if (entry / 'publish.complete').is_file():
        published_framework(entry)
        return
    with (entry / 'publish.log').open('a') as log:
        log.write(f'\n--- Publish attempt {datetime.now(UTC).isoformat()} ---\n')
        log.flush()
        print(f'Publishing mixed AOT; log: {entry / "publish.log"}', flush=True)
        try:
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
                    f'-bl:{entry / "publish.binlog"}',
                ],
                cwd=entry / 'candidate',
                env=sdk_environment(sdk),
                stdout=log,
                stderr=subprocess.STDOUT,
                check=True,
                pass_fds=(lock.fileno(),),
            )
        except subprocess.CalledProcessError as error:
            raise ValueError(f'AOT publish failed; retry to resume compilation. See {entry / "publish.log"}') from error
    published_framework(entry)
    (entry / 'publish.complete').touch()


def prepare(bundle: Path, prepared: Path, cache: Path, sdk_path: Path) -> Path:
    """Cache native compilation separately from current private assets and loader."""
    sdk, sdk_identity = toolchain(sdk_path)
    dlls = assemblies(prepared)
    main = published_loader((prepared / 'main.js').read_text())
    sources = compiler_inputs(bundle / 'aot-src')
    inputs = {f'assembly/{name}.dll': path for name, path in dlls.items()}
    inputs.update({f'source/{path.name}': path for path in sources})
    # This module owns generated properties, roots, targets, and export adaptation.
    inputs['compiler/aot.py'] = Path(__file__)
    key = digest_files(inputs, sdk_identity)
    compilation = cache / 'aot-builds' / key
    compilation.mkdir(parents=True, exist_ok=True, mode=0o700)
    # Hold through composition: publication and selection must observe one success.
    print(f'Checking mixed AOT cache: {compilation}', flush=True)
    with build_lock(compilation) as lock:
        candidate = compilation / 'candidate'
        if not candidate.is_dir():
            with tempfile.TemporaryDirectory(prefix='.candidate-', dir=compilation) as temporary:
                project = Path(temporary) / 'candidate'
                write_candidate(bundle / 'aot-src', project, dlls, sdk_identity['target_runtime'])
                (project / 'global.json').write_text(sdk_selection(sdk_identity['version']))
                project.rename(candidate)
        compile_entry(compilation, sdk, lock)
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
            shutil.copytree(prepared, root)
            shutil.rmtree(root / '_framework')
            shutil.copytree(published_framework(compilation), root, dirs_exist_ok=True)
            (root / 'main.js').write_text(main)
            (root / '.ready').write_text(runtime_key + '\n')
            root.rename(destination)
        return destination
