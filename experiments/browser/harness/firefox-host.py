"""Keep local endpoint credentials in the host environment of the Firefox probe."""

import os
import sys
import tomllib
from pathlib import Path

root = Path(__file__).resolve().parents[3]
config = Path(os.environ.get('XDG_CONFIG_HOME', Path.home() / '.config')) / 'slopworld'
descriptor = tomllib.loads((config / 'endpoints/local.toml').read_text())
environment = dict(os.environ, SLOPD_URL=descriptor['url'], SLOPD_TOKEN=descriptor['token'])
os.execvpe(
    'node',
    ['node', str(root / 'experiments/browser/harness/firefox-benchmark.mjs'), *sys.argv[1:]],
    environment,
)
