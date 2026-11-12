"""Validate measured WASM behavior and local assembly provenance, without game code."""

import hashlib
import json
import math
import sys
from pathlib import Path

game, root, report = map(Path, sys.argv[1:])
evidence = json.loads(report.read_text())
assert evidence['completed'], 'Browser did not complete'
result = evidence['result']
assert result['browser'] and result['architecture'] == 'Wasm'
assert not result['loadErrors'], result['loadErrors']
checks = {check['name']: check for check in result['checks']}
for name, value in {
    'game.positive-mod': '3',
    'game.cell-length': '25',
    'unity.math': '9',
    'runtime.dynamic-method': '42',
}.items():
    assert checks[name]['ok'] and checks[name]['value'] == value, checks[name]
assert checks['game.version']['ok']
assert not checks['game.entrypoint']['ok'], 'Startup behavior changed; inspect evidence'
scene = result['scene']
assert len(scene['cells']) == 192 and len(scene['vertices']) == 192 * 6 * 14
assert all(math.isfinite(value) for value in scene['vertices'])
for cell in scene['cells']:
    assert cell['y'] == 0
    assert cell['lengthSquared'] == cell['x'] ** 2 + cell['z'] ** 2
manifest = json.loads((root / 'game/manifest.json').read_text())
assert result['manifest'] == manifest, 'Report does not describe current staged input'
assert len(result['loaded']) == len(manifest)
changed = set()
for item in manifest:
    original = hashlib.sha256((game / item['file']).read_bytes()).hexdigest()
    staged = hashlib.sha256((root / 'game' / item['file']).read_bytes()).hexdigest()
    assert original == item['sha256'], item['file']
    assert staged == item['stagedSha256'], item['file']
    if original != staged:
        changed.add(item['file'])
if result['adaptations']:
    assert changed == {'RimWorld.dll', 'UnityStubs.dll', 'Brutal.Core.Common.dll'}
    deep = {check['name']: check for check in result['deep']['checks']}
    assert any('DllNotFoundException: glfw' in error for error in deep['engine.constructor']['errors'])
else:
    assert not changed
print(f'Verified WASM computations, geometry, and {len(manifest)} assembly hashes; {len(changed)} adapted.')
