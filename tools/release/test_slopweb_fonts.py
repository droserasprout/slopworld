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
    'value', [{'fontSize': 7}, {'fontSize': True}, {'uiFontSize': 49}, {'fontName': 'bad\n'}, {'density': 3}]
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
        else:
            Path(args[4]).write_text('{}')

    monkeypatch.setattr(fonts.subprocess, 'run', probe)
    selected = {'uiFontSize': 0, 'density': 5 / 3}
    root = fonts.prepare(bundle, game, cache, selected)
    assert len(calls) == 18
    assert {int(args[5]) for args in calls[2:-4]} == {11, 13, 15}
    assert all(args[6] == str(5 / 3) for args in calls[2:])
    assert len(json.loads((root / 'manifest.json').read_text())) == 16
    assert fonts.prepare(bundle, game, cache, selected) == root
    assert len(calls) == 18

    def fail(*args: object, **kwargs: object) -> None:
        raise subprocess.CalledProcessError(1, 'probe')

    monkeypatch.setattr(fonts.subprocess, 'run', fail)
    with pytest.raises(subprocess.CalledProcessError):
        fonts.prepare(bundle, game, cache, {'fontName': 'failure'})
    assert (root / 'manifest.json').is_file()
    assert not list(cache.glob('.bake-*'))


def test_prepared_runtime_fits_atlas_budget() -> None:
    root = ROOT / 'experiments/browser/runtime/out/wwwroot-eco/assets/fonts'
    if not (root / 'manifest.json').is_file():
        pytest.skip('Private native font bakes are not present')
    total = 0
    for face in json.loads((root / 'manifest.json').read_text()):
        atlas = json.loads((root / face['bake']).read_text())['atlas']
        total += atlas['width'] * atlas['height'] * 4
    assert total <= 384 * 1024 * 1024
