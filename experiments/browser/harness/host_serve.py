"""Serve locally prepared WASM using the host's local daemon descriptor."""

import json
import os
import subprocess
import sys
import tomllib
from pathlib import Path
from urllib.parse import quote

root = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(root / 'slopweb'))
from appearance import preferences
from appearance import write_preferences
from portraits import host_snapshot
from portraits import read_snapshot

os.chdir(root)
prepared = root / 'experiments/browser/runtime/out/wwwroot-eco'
if sys.argv[1:2] in (['--appearance'], ['--check-workspace'], ['--reference']) or (
    len(sys.argv) > 1 and Path(sys.argv[1]).resolve() == prepared
):
    write_preferences(prepared, preferences())
if sys.argv[1:2] == ['--appearance']:
    print('Updated allowlisted host presentation settings; refresh the browser to apply them.')
    sys.exit(0)
if sys.argv[1:2] == ['--portraits']:
    snapshot = read_snapshot(Path(sys.argv[2])) if len(sys.argv) > 2 and sys.argv[2] else host_snapshot()
    snapshot.write(prepared)
    print('Imported ' + str(len(snapshot.files)) + ' validated local portrait pixel files.')
    sys.exit(0)
config = Path(os.environ.get('XDG_CONFIG_HOME', Path.home() / '.config')) / 'slopworld'
descriptor = tomllib.loads((config / 'endpoints/local.toml').read_text())
environment = dict(os.environ, SLOPD_URL=descriptor['url'], SLOPD_TOKEN=descriptor['token'])
if sys.argv[1:2] in (['--check-workspace'], ['--check-installed']):
    installed = sys.argv[1] == '--check-installed'
    color_type = environment.get('PROBE_CANVAS_COLOR_TYPE', 'unorm8')
    if color_type not in ('unorm8', 'float16'):
        raise ValueError('Expected unorm8 or float16 Canvas')
    parameters = ('session=' + quote(sys.argv[2], safe='')) if not installed and len(sys.argv) > 2 else ''
    if color_type == 'float16':
        parameters += '&floatCanvas'
    page = ('/' if installed else '/workspace.html') + ('?' + parameters if parameters else '')
    environment.update(
        PROBE_WIDTH=environment.get('PROBE_WIDTH', '1536'),
        PROBE_HEIGHT=environment.get('PROBE_HEIGHT', '864'),
        PROBE_DPR=environment.get('PROBE_DPR', str(5 / 3)),
        PROBE_PROGRESS='1',
        PROBE_COMMAND_TIMEOUT_MS='900000',
        PROBE_TIMEOUT_MS='1800000',
    )
    if sys.argv[3:4] == ['1']:
        environment['PROBE_KEEP_OPEN'] = '1'
    os.execvpe(
        'node',
        [
            'node',
            str(root / 'experiments/browser/harness/validate.mjs'),
            str(Path(sys.argv[2]).resolve() if installed else root / 'experiments/browser/runtime/out/wwwroot-eco'),
            page,
            str(
                root
                / 'experiments/browser/artifacts'
                / ('installed-startup.json' if installed else 'workspace-startup.json')
            ),
        ],
        environment,
    )
if sys.argv[1:2] == ['--reference']:
    appearance = preferences()
    if sys.argv[3:4] == ['nativeFonts']:
        fonts = root / 'experiments/browser/runtime/out/wwwroot-eco/assets/fonts'
        recipes = root / 'experiments/browser/runtime/justfile'
        family = appearance.get('fontName') or 'monospace'
        subprocess.run(['just', '--justfile', str(recipes), 'font-resolve', family, str(fonts)], check=True)
        subprocess.run(
            [
                'just',
                '--justfile',
                str(recipes),
                'font-probe',
                str(fonts / 'terminal.ttf'),
                str(fonts / 'terminal.json'),
                str(appearance.get('fontSize', 13)),
                str(5 / 3),
            ],
            check=True,
        )
        manifest_path = fonts / 'manifest.json'
        manifest = [face for face in json.loads(manifest_path.read_text()) if face['font'] != 'terminal.ttf']
        manifest.append({'font': 'terminal.ttf', 'bake': 'terminal.json'})
        manifest_path.write_text(json.dumps(manifest))
    write_preferences(prepared, appearance)
    environment.update(
        PROBE_WIDTH='1536',
        PROBE_HEIGHT='864',
        PROBE_DPR=str(5 / 3),
        PROBE_KEEP_OPEN='1',
        PROBE_PROGRESS='1',
        PROBE_COMMAND_TIMEOUT_MS='900000',
        PROBE_TIMEOUT_MS='1800000',
    )
    os.execvpe(
        'node',
        [
            'node',
            str(root / 'experiments/browser/harness/validate.mjs'),
            str(root / 'experiments/browser/runtime/out/wwwroot-eco'),
            '/index.html?entry&assets&eco&workspace&live&app&referenceAppearance&width=2560&height=1440&uiScale=1.666666667'
            + ('&session=' + quote(sys.argv[2], safe='') if len(sys.argv) > 2 and sys.argv[2] else '')
            + ('&nativeFonts' if sys.argv[3:4] == ['nativeFonts'] else ''),
            str(root / 'experiments/browser/artifacts/workspace-reference.json'),
        ],
        environment,
    )
if sys.argv[1:] == ['--check']:
    environment['REQUIRE_WORKSPACE_WRITE_ACCESS'] = '1'
    os.execvpe('node', ['node', str(root / 'experiments/browser/harness/bridge-check.mjs')], environment)
os.execvpe(
    'node', ['node', str(root / 'experiments/browser/harness/server.mjs'), sys.argv[1], sys.argv[2]], environment
)
