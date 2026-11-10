"""Condense local PE evidence; native.json retains every discovered import."""

import json
from collections import Counter
from pathlib import Path

reports = json.loads(Path('out/native.json').read_text())
for report in reports:
    imports = report['imports']
    counts = Counter(item['library'] for item in imports)
    print(report['assembly'], 'imports', len(imports), dict(counts))
    if report['assembly'] == 'Waylith.dll':
        for row in report['types']:
            if row['name'] in (
                'Waylith.Graphics.IRenderContext',
                'Waylith.Graphics.IRenderFrameState',
                'Waylith.Rendering.IRenderPass',
                'Waylith.Rendering.IRenderFeature',
            ):
                print('  interface:', row['name'], '\n    '.join(row['signatures']))
        counts = Counter(item['type'].rsplit('.', 1)[0] for item in report['members'])
        print('  member reference counts (not runtime traces):', dict(counts))
        for row in report['members']:
            if any(
                word in row['name'] for word in ('Create', 'Draw', 'Dispatch', 'Push', 'Bind', 'Present', 'Barrier')
            ):
                print('  member:', row['type'], row['name'])
