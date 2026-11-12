"""Build the isolated FreeType WASM module using Emscripten on PATH."""

import ast
import fcntl
import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

root = Path(__file__).resolve().parent
cache = root.parent / 'out/local-font-toolchain'
cache.mkdir(parents=True, exist_ok=True)
sdk = cache / 'emsdk'
if '--install-sdk' in sys.argv:
    if not sdk.exists():
        subprocess.run(
            [
                'git',
                'clone',
                '--depth',
                '1',
                '--branch',
                '7.0.0',
                'https://github.com/emscripten-core/emsdk.git',
                str(sdk),
            ],
            check=True,
        )
    subprocess.run([str(sdk / 'emsdk'), 'install', '7.0.0'], check=True)
    subprocess.run([str(sdk / 'emsdk'), 'activate', '7.0.0'], check=True)
    sys.exit(0)
env = dict(os.environ, EM_CACHE=str(cache / 'system-cache'))
output = root.parent / 'wwwroot/local-fonts.mjs'
if sdk.exists():
    compiler = sdk / 'upstream/emscripten/emcc'
    env['EM_CONFIG'] = str(sdk / '.emscripten')
else:
    selected = shutil.which('emcc')
    if not selected:
        raise SystemExit('Emscripten 7.0.0 is required; run just web-local-fonts-sdk')
    compiler = Path(selected).resolve()
# Emscripten verifies these source archive hashes before compiling. Refuse an
# unreviewed port upgrade rather than silently changing raster output on rebuild.
for port, expected in {
    'freetype': '460ea4dc9cc879822556d801341a1cf3efcd57a13cd4da64169603ea98873337dd19e3c8c6d9054b27d10acbbaa8d5c59b7f560c697b98dc2640bcd88f57554a',
    'zlib': '16fea4df307a68cf0035858abe2fd550250618a97590e202037acd18a666f57afc10f8836cbbd472d54a0e76539d0e558cb26f059d53de52ff90634bbf4f47d4',
}.items():
    tree = ast.parse((compiler.parent / 'tools/ports' / (port + '.py')).read_text())
    actual = next(
        ast.literal_eval(n.value)
        for n in tree.body
        if isinstance(n, ast.Assign) and any(isinstance(t, ast.Name) and t.id == 'HASH' for t in n.targets)
    )
    if actual != expected:
        raise ValueError(f'Unreviewed {port} port; run fidelity checks before updating its source pin')
# Include flags (this script), port definitions, SDK configuration and compiler
# identity. Verify all outputs, so deleted/corrupt artifacts never become hits.
identity = hashlib.sha256()
for path in (
    Path(__file__),
    root / 'raster.c',
    compiler,
    compiler.parent / 'tools/ports/freetype.py',
    compiler.parent / 'tools/ports/zlib.py',
):
    identity.update(path.read_bytes())
configuration = Path(env.get('EM_CONFIG', str(Path.home() / '.emscripten')))
if configuration.is_file():
    identity.update(configuration.read_bytes())
identity.update(subprocess.run([str(compiler), '--version'], env=env, check=True, capture_output=True).stdout)
identity.update(
    json.dumps(
        {name: value for name, value in sorted(env.items()) if name.startswith(('EM_', 'EMCC_', 'LLVM_', 'BINARYEN_'))},
        sort_keys=True,
    ).encode()
)
key = identity.hexdigest()
outputs = [output, output.with_suffix('.wasm'), output.with_name('local-fonts.LICENSE.txt')]
marker = cache / 'raster-build.json'
with (cache / 'raster-build.lock').open('a') as lock:
    fcntl.flock(lock, fcntl.LOCK_EX)
    try:
        saved = json.loads(marker.read_text())
    except (OSError, ValueError):
        saved = {}
    if not isinstance(saved, dict):
        saved = {}
    if saved.get('key') == key and all(path.is_file() for path in outputs):
        hashes = {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in outputs}
        if saved.get('outputs') == hashes:
            print('Local font rasterizer cache hit')
            sys.exit(0)
    marker.unlink(missing_ok=True)
    with tempfile.TemporaryDirectory(prefix='.raster-', dir=cache) as temporary:
        output = Path(temporary) / output.name
        subprocess.run(
            [
                str(compiler),
                str(root / 'raster.c'),
                '-O2',
                '-sUSE_FREETYPE=1',
                '-sMODULARIZE=1',
                '-sEXPORT_ES6=1',
                '-sENVIRONMENT=web,node',
                '-sALLOW_MEMORY_GROWTH=1',
                '-sMAXIMUM_MEMORY=268435456',
                '-sFILESYSTEM=0',
                '-sSUPPORT_LONGJMP=wasm',
                '-fwasm-exceptions',
                '-sEXPORTED_FUNCTIONS=["_malloc","_free","_font_open","_font_close","_font_shutdown","_font_size","_font_glyph","_font_result"]',
                '-sEXPORTED_RUNTIME_METHODS=["HEAPU8","HEAP32","HEAPF32"]',
                '-o',
                str(output),
            ],
            env=env,
            check=True,
        )
        ports = cache / 'system-cache/ports'
        freetype = ports / 'freetype/freetype-VER-2-14-3'
        notices = ['This software uses the FreeType Project (https://freetype.org), under the FreeType License.']
        for path in [
            freetype / name
            for name in ('LICENSE.TXT', 'docs/FTL.TXT', 'src/bdf/README', 'src/pcf/README', 'src/autofit/ft-hb-ft.c')
        ]:
            notices.append(path.read_text())
        notices.extend(path.read_text() for path in [ports / 'zlib/zlib-1.3.2/LICENSE', compiler.parent / 'LICENSE'])
        output.with_name('local-fonts.LICENSE.txt').write_text('\n\n'.join(notices))

        for destination in outputs:
            shutil.copyfile(output.with_name(destination.name), destination)
        hashes = {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in outputs}
        pending = marker.with_suffix('.tmp')
        pending.write_text(json.dumps({'key': key, 'outputs': hashes}))
        pending.replace(marker)
