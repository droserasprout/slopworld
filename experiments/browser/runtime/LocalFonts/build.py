"""Build the isolated FreeType WASM module using Emscripten on PATH."""

import ast
import os
import shutil
import subprocess
import sys
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
        raise SystemExit('Emscripten 7.0.0 is required; run just browser-local-fonts-sdk')
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
