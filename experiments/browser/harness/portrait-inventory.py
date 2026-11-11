"""Report private portrait preparation inventories without loading pawn/save data."""

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / 'slopweb'))
from portraits import exporter_available
from portraits import host_source
from portraits import read_snapshot

source = host_source()
print(json.dumps({'source': str(source), 'exporterAvailable': exporter_available(source)}))
inspection_root = Path(sys.argv[1]) if len(sys.argv) > 1 and sys.argv[1] else ROOT
locations = [source / 'manifest.json']
for base in (
    Path.home() / '.cache/slopworld',
    Path.home() / '.cache/slopweb',
    inspection_root / 'experiments/browser/runtime/out',
    inspection_root / 'experiments/browser/artifacts',
):
    if base.exists():
        locations.extend(base.rglob('portraits/manifest.json'))
        locations.extend(base.rglob('workspace-portraits/*/manifest.json'))
for path in sorted(set(locations)):
    try:
        snapshot = read_snapshot(path.parent)
    except (ValueError, OSError) as error:
        print(json.dumps({'manifest': str(path), 'error': str(error)}))
        continue
    print(
        json.dumps(
            {
                'manifest': str(path),
                'exists': path.exists(),
                'entries': len(json.loads(snapshot.manifest)['entries']),
                'identity': snapshot.identity,
            }
        )
    )
