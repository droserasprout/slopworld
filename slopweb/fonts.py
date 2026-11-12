"""Bounded, atomic host font discovery and file delivery. Paths and resolver diagnostics stay on the host."""

import argparse
import fcntl
import hashlib
import json
import math
import os
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path
from typing import Any


def selection(value: dict[str, Any]) -> dict[str, Any]:
    if 'face' in value:
        raise ValueError('Host glyph rasterization is not supported')
    result: dict[str, Any] = {}
    for name, default in (('uiFontName', ''), ('fontName', 'monospace')):
        text = value.get(name, default)
        if not isinstance(text, str) or len(text) > 256 or any(ord(c) < 32 for c in text):
            raise ValueError('Invalid font family')
        result[name] = text
    for name, default_size, low, high in (('uiFontSize', 14, 0, 48), ('fontSize', 14, 8, 28)):
        size = value.get(name, default_size)
        if type(size) is not int or not low <= size <= high:
            raise ValueError('Invalid font size')
        result[name] = size
    density = value.get('density', 1)
    if type(density) not in (int, float) or not math.isfinite(density) or not 0.5 <= density <= 4:
        raise ValueError('Invalid font density')
    result['density'] = density
    return result


def prepare(bundle: Path, game: Path, cache: Path, value: dict[str, Any]) -> Path:
    values = selection(value)
    identity = [(str(game), (bundle / 'BUILD_ID').read_text()), values]
    key = hashlib.sha256(json.dumps(identity, sort_keys=True).encode()).hexdigest()
    cache.mkdir(parents=True, exist_ok=True, mode=0o700)
    with (cache / '.lock').open('w') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        target = cache / key
        if (target / 'manifest.json').is_file():
            (target / 'manifest.json').touch()
            return target
        entries = sorted(cache.glob('*/manifest.json'), key=lambda path: path.stat().st_mtime)
        for entry in entries[:-63]:
            shutil.rmtree(entry.parent)
        with tempfile.TemporaryDirectory(prefix='.bake-', dir=cache) as temporary:
            root = Path(temporary)
            env = os.environ.copy()
            env['LD_LIBRARY_PATH'] = str(game) + (':' + env['LD_LIBRARY_PATH'] if env.get('LD_LIBRARY_PATH') else '')

            deadline = time.monotonic() + 160

            def probe(*args: object) -> None:
                subprocess.run(
                    ['dotnet', str(bundle / 'font-probe/FontProbe.dll'), str(game), *map(str, args)],
                    check=True,
                    env=env,
                    stdout=sys.stderr,
                    timeout=max(0.1, min(30, deadline - time.monotonic())),
                )

            manifest = []
            probe('--resolve-ui', bundle / 'mod/SlopWorld.dll', values['uiFontName'], root)
            probe('--resolve', bundle / 'mod/SlopWorld.dll', values['fontName'], root)
            config = json.loads((root / 'font-config.json').read_text())
            for font in ('ui.ttf', 'terminal.ttf'):
                sizes = (
                    [max(1, (values['uiFontSize'] or 13) + offset) for offset in (-2, 0, 2)]
                    if font == 'ui.ttf'
                    else [values['fontSize']]
                )
                for size in dict.fromkeys(sizes):
                    descriptor = f'{font}-{size}.json'
                    (root / descriptor).write_text(json.dumps({**config, 'size': size}))
                    manifest.append({'font': font, 'bake': descriptor})
            (root / 'manifest.json').write_text(json.dumps(manifest))
            root.rename(target)
    return target


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('game', type=Path)
    parser.add_argument('cache', type=Path)
    parser.add_argument('selection')
    args = parser.parse_args()
    os.umask(0o077)
    print(prepare(Path(__file__).resolve().parent, args.game, args.cache, json.loads(args.selection)))
