"""Build-script cache contracts for the local WASM rasterizer."""

import subprocess
from pathlib import Path

import pytest

from tools import ROOT


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
