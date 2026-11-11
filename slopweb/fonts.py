"""Bounded, atomic host font bakes. Paths and resolver diagnostics stay on the host."""

import argparse
import fcntl
import hashlib
import json
import os
import subprocess
import tempfile
import time
from pathlib import Path


def selection(value: dict) -> dict:
    result = {}
    for name, default in (('uiFontName', ''), ('fontName', 'monospace')):
        text = value.get(name, default)
        if not isinstance(text, str) or len(text) > 256 or any(ord(c) < 32 for c in text):
            raise ValueError('Invalid font family')
        result[name] = text
    for name, default, low, high in (('uiFontSize', 14, 0, 48), ('fontSize', 14, 8, 28)):
        size = value.get(name, default)
        if type(size) is not int or not low <= size <= high:
            raise ValueError('Invalid font size')
        result[name] = size
    density = value.get('density', 1)
    if type(density) not in (int, float) or density not in (1, 5 / 3, 2):
        raise ValueError('Invalid font density')
    result['density'] = density
    return result


def prepare(bundle: Path, game: Path, cache: Path, value: dict) -> Path:
    values = selection(value)
    identity = [(str(game), (bundle / 'BUILD_ID').read_text()), values]
    key = hashlib.sha256(json.dumps(identity, sort_keys=True).encode()).hexdigest()
    cache.mkdir(parents=True, exist_ok=True, mode=0o700)
    with (cache / '.lock').open('w') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        target = cache / key
        if (target / 'manifest.json').is_file():
            return target
        if len(list(cache.glob('*/manifest.json'))) >= 64:
            raise ValueError('Font cache is full; restart after clearing the private fonts cache')
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
                    stdout=subprocess.DEVNULL,
                    timeout=max(0.1, min(30, deadline - time.monotonic())),
                )

            probe('--resolve-ui', bundle / 'mod/SlopWorld.dll', values['uiFontName'], root)
            probe('--resolve', bundle / 'mod/SlopWorld.dll', values['fontName'], root)
            manifest = []
            # Bake only the requested prepared density. Resize events request a
            # new variant; ordinary drawing only selects installed textures.
            ui_size = values['uiFontSize'] or 13
            for font, sizes in (
                ('ui.ttf', {max(1, ui_size + offset) for offset in (-2, 0, 2)}),
                ('terminal.ttf', {values['fontSize']}),
            ):
                for size in sorted(sizes):
                    for style in range(4):
                        bake = f'{font}-{size}-{style}.json'
                        probe(root / font, root / bake, size, values['density'], style)
                        manifest.append({'font': font, 'bake': bake})
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
