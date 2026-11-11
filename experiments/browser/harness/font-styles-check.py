"""Check native style bakes without confusing rendering modes with font styles."""

import json
import sys
from pathlib import Path


def advances(face: dict) -> dict[int, float]:
    return {glyph['scalar']: glyph['advance'] for glyph in face['glyphs'] if not glyph['fallback']}


root = Path(sys.argv[1]) / 'assets/fonts'
groups: dict[tuple, dict[int, dict]] = {}
for entry in json.loads((root / 'manifest.json').read_text()):
    bake = json.loads((root / entry['bake']).read_text())
    key = (entry['font'], bake['size'], bake['density'])
    groups.setdefault(key, {})[bake['style']] = bake
for key, styles in groups.items():
    assert set(styles) == {0, 1, 2, 3}, (key, 'missing styles')
    normal, bold, italic, combined = (styles[i] for i in range(4))
    assert len({face['atlas']['pixels'] for face in styles.values()}) == 4, (key, 'styles share raster ink')
    assert all(face['lineMetrics'] == normal['lineMetrics'] for face in styles.values()), (key, 'line metrics changed')
    assert bold['loaderFlags'] != normal['loaderFlags'], (key, 'bold flags absent')
    assert italic['loaderFlags'] != normal['loaderFlags'], (key, 'oblique flags absent')
    assert combined['loaderFlags'] == bold['loaderFlags'] | italic['loaderFlags'], (key, 'combined flags disagree')
    assert advances(normal) == advances(italic), (key, 'oblique changed advances')
print(json.dumps({'nativeStyleGroups': len(groups), 'stylesDistinct': True, 'italicMetricsPreserved': True}))
