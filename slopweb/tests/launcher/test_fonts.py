"""Game-free validation of bounded dynamic font preparation."""

import importlib.util
import json
import subprocess
from pathlib import Path

import pytest

from tools import ROOT

spec = importlib.util.spec_from_file_location('slopweb_fonts', ROOT / 'slopweb/fonts.py')
assert spec and spec.loader
fonts = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fonts)


@pytest.mark.parametrize(
    'value', [{'fontSize': 7}, {'fontSize': True}, {'uiFontSize': 49}, {'fontName': 'bad\n'}, {'density': 5}]
)
def test_selection_rejects_invalid_input(value: dict[str, object]) -> None:
    with pytest.raises(ValueError):
        fonts.selection(value)


def test_atomic_bake_cache_and_recovery(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    bundle, game, cache = (tmp_path / name for name in ('bundle', 'game', 'cache'))
    bundle.mkdir()
    (bundle / 'BUILD_ID').write_text('build')
    calls = []

    def probe(args: list[str], **kwargs: object) -> None:
        calls.append(args)
        assert kwargs['timeout'] == 30
        if args[3].startswith('--resolve'):
            root = Path(args[-1])
            (root / ('ui.ttf' if args[3] == '--resolve-ui' else 'terminal.ttf')).write_bytes(b'font')
            (root / 'chains.json').write_text('{}')
            (root / 'font-config.json').write_text('{"loaderFlags":513}')
        else:
            Path(args[4]).write_text('{}')

    monkeypatch.setattr(fonts.subprocess, 'run', probe)
    selected = {'uiFontSize': 0, 'density': 5 / 3}
    root = fonts.prepare(bundle, game, cache, selected)
    assert len(calls) == 2
    assert all(args[3].startswith('--resolve') for args in calls)
    manifest = json.loads((root / 'manifest.json').read_text())
    assert [json.loads((root / face['bake']).read_text())['size'] for face in manifest] == [11, 13, 15, 14]
    assert fonts.prepare(bundle, game, cache, selected) == root
    assert len(calls) == 2

    def fail(*args: object, **kwargs: object) -> None:
        raise subprocess.CalledProcessError(1, 'probe')

    monkeypatch.setattr(fonts.subprocess, 'run', fail)
    with pytest.raises(subprocess.CalledProcessError):
        fonts.prepare(bundle, game, cache, {'fontName': 'failure'})
    assert (root / 'manifest.json').is_file()
    assert not list(cache.glob('.bake-*'))


@pytest.mark.parametrize('runtime', ['wwwroot-eco', 'installed-font-check'])
def test_prepared_runtime_fits_atlas_budget(runtime: str) -> None:
    root = ROOT / 'slopweb/runtime/out' / runtime / 'assets/fonts'
    if not (root / 'manifest.json').is_file():
        pytest.skip('Private native font bakes are not present')
    total = 0
    for face in json.loads((root / 'manifest.json').read_text()):
        atlas = json.loads((root / face['bake']).read_text())['atlas']
        total += atlas['width'] * atlas['height'] * 4
    assert total <= 384 * 1024 * 1024


def test_host_rejects_glyph_requests() -> None:
    with pytest.raises(ValueError, match='not supported'):
        fonts.selection({'face': {}})


def test_rasterizer_cache_detects_changed_inputs_missing_outputs_and_failed_compile(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    import re
    import runpy
    import sys

    script = tmp_path / 'LocalFonts/build.py'
    script.parent.mkdir()
    script.write_text((ROOT / 'slopweb/runtime/LocalFonts/build.py').read_text())
    (script.parent / 'raster.c').write_text('source')
    output = tmp_path / 'wwwroot'
    output.mkdir()
    cache = tmp_path / 'out/local-font-toolchain'
    compiler = cache / 'emsdk/upstream/emscripten/emcc'
    compiler.parent.mkdir(parents=True)
    compiler.write_text('compiler')
    for name, value in re.findall(r"'(freetype|zlib)': '([0-9a-f]+)'", script.read_text()):
        port = compiler.parent / 'tools/ports' / (name + '.py')
        port.parent.mkdir(parents=True, exist_ok=True)
        port.write_text(f'HASH = {value!r}')
    for name in ('LICENSE.TXT', 'docs/FTL.TXT', 'src/bdf/README', 'src/pcf/README', 'src/autofit/ft-hb-ft.c'):
        path = cache / 'system-cache/ports/freetype/freetype-VER-2-14-3' / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text('license')
    license_path = cache / 'system-cache/ports/zlib/zlib-1.3.2/LICENSE'
    license_path.parent.mkdir(parents=True)
    license_path.write_text('license')
    (compiler.parent / 'LICENSE').write_text('license')
    calls = []
    fail = False

    def compile_module(args: list[str], **kwargs: object) -> subprocess.CompletedProcess[bytes]:
        if '--version' in args:
            return subprocess.CompletedProcess(args, 0, b'7.0.0')
        calls.append(args)
        if fail:
            raise subprocess.CalledProcessError(1, args)
        result = Path(args[args.index('-o') + 1])
        result.write_text('module')
        result.with_suffix('.wasm').write_bytes(b'wasm')
        return subprocess.CompletedProcess(args, 0)

    monkeypatch.setattr(subprocess, 'run', compile_module)
    monkeypatch.setattr(sys, 'argv', [str(script)])

    def build() -> None:
        try:
            runpy.run_path(str(script))
        except SystemExit as error:
            assert error.code == 0

    build()
    build()
    assert len(calls) == 1
    (output / 'local-fonts.wasm').unlink()
    build()
    assert len(calls) == 2
    (output / 'local-fonts.mjs').write_text('corrupted')
    build()
    assert len(calls) == 3
    (script.parent / 'raster.c').write_text('changed source')
    fail = True
    with pytest.raises(subprocess.CalledProcessError):
        build()
    assert not (cache / 'raster-build.json').exists()
    assert (output / 'local-fonts.mjs').read_text() == 'module'
    fail = False
    build()
    build()
    assert len(calls) == 5
    (cache / 'raster-build.json').write_text('[]')
    build()
    build()
    assert len(calls) == 6
