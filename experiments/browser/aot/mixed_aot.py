"""Private mixed-AOT snapshots; never prepare game IL or change launcher output."""

import argparse
import json
import shutil
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
PACKAGES = {
    'Newtonsoft.Json': '13.0.3',
    'Tmds.DBus.Protocol': '0.95.1',
    'StbImageSharp': '2.27.13',
    'StbImageWriteSharp': '1.16.7',
}
INTERPRETED = ('Waylith', 'Waylith.Injection', 'RimWorldWaylith.PrepatcherCompat')


def private_output(output: Path) -> Path:
    output = output.resolve()
    private = REPO / 'experiments/browser/artifacts'
    if output == private or private not in output.parents:
        raise ValueError('Output must be a new child of experiments/browser/artifacts')
    return output


def checked_sdk(sdk: Path) -> Path:
    sdk = sdk.resolve(strict=True)
    if not sdk.is_file() or sdk.name != 'dotnet':
        raise ValueError('Pass the existing wasm-tools SDK dotnet executable')
    return sdk


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


def prepare(prepared: Path, output: Path, sdk: Path) -> None:
    sdk = checked_sdk(sdk)
    prepared = prepared.resolve(strict=True)
    output = private_output(output)
    if output == prepared or prepared in output.parents or output in prepared.parents:
        raise ValueError('Prepared root and output must be disjoint')
    if output.exists():
        raise FileExistsError(output)
    for required in ('main.js', 'game/manifest.json', 'game/Assembly-CSharp.dll', 'mod/SlopWorld.dll', 'Adapters.dll'):
        if not (prepared / required).is_file():
            raise ValueError(f'Missing final prepared input: {required}')
    # Reject aliases before copytree follows links, including links to host secrets.
    if any(path.is_symlink() for path in prepared.rglob('*')):
        raise ValueError('Prepared snapshots must not contain symlinks')
    main = published_loader((prepared / 'main.js').read_text())
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

    # Exclusive creation reserves output. On failure keep diagnostics; never delete a caller path.
    output.parent.mkdir(parents=True, exist_ok=True)
    output.mkdir()
    shutil.copytree(prepared, output / 'snapshot')
    (output / 'snapshot/main.js').write_text(main)
    candidate = output / 'candidate'
    assemblies = candidate / 'assemblies'
    assemblies.mkdir(parents=True)
    runtime = REPO / 'experiments/browser/runtime'
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
    (output / 'experiment.json').write_text(json.dumps({'sdk': str(sdk), 'prepared_root': str(prepared)}, indent=2))


def publish(output: Path, sdk: Path) -> None:
    output = private_output(output)
    sdk = checked_sdk(sdk)
    manifest = json.loads((output / 'experiment.json').read_text())
    if manifest['sdk'] != str(sdk):
        raise ValueError('Publish must use the SDK selected during prepare')
    # The exclusive log prevents overlapping/repeated publishes into one candidate.
    with (output / 'publish.log').open('x') as log:
        subprocess.run(
            [
                str(sdk),
                'publish',
                str(output / 'candidate/Probe.csproj'),
                '-c',
                'Release',
                '-o',
                str(output / 'published'),
                '--nologo',
                '-v',
                'minimal',
                '-p:RestoreLockedMode=false',
                f'-bl:{output / "publish.binlog"}',
            ],
            cwd=output / 'candidate',
            stdout=log,
            stderr=subprocess.STDOUT,
            check=True,
        )
    (output / 'publish.complete').touch()


def stage(output: Path) -> None:
    output = private_output(output)
    if not (output / 'publish.complete').is_file():
        raise ValueError('Publish must finish successfully before staging')
    published = output / 'published/wwwroot'
    if not (published / '_framework/dotnet.js').is_file() or not list(
        (published / '_framework').glob('dotnet.native*.wasm')
    ):
        raise ValueError('Published framework is incomplete')
    destination = output / 'wwwroot'
    destination.mkdir()  # Refuse to merge with an old runtime.
    shutil.copytree(output / 'snapshot', destination, dirs_exist_ok=True)
    shutil.rmtree(destination / '_framework')
    shutil.copytree(published, destination, dirs_exist_ok=True)
    shutil.copy2(output / 'snapshot/main.js', destination / 'main.js')


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='action', required=True)
    for action in ('prepare', 'publish', 'stage'):
        command = sub.add_parser(action)
        command.add_argument('--output', type=Path, required=True)
        if action != 'stage':
            command.add_argument('--sdk', type=Path, required=True)
        if action == 'prepare':
            command.add_argument('--prepared-root', type=Path, required=True)
    args = parser.parse_args()
    if args.action == 'prepare':
        prepare(args.prepared_root, args.output, args.sdk)
    elif args.action == 'publish':
        publish(args.output, args.sdk)
    else:
        stage(args.output)


if __name__ == '__main__':
    main()
