"""Prepare host presentation inputs in the private runtime, never the package."""

import argparse
import json
import os
import subprocess
import tomllib
from collections.abc import Mapping
from pathlib import Path
from typing import Any


def preferences() -> dict[str, Any]:
    data = Path(os.environ.get('XDG_DATA_HOME', str(Path.home() / '.local/share')))
    profile = data / 'slopworld/profiles/local/Config/SlopWorld.toml'
    values = tomllib.loads(profile.read_text()) if profile.is_file() else {}
    allowed = {
        'sidebarHidden',
        'sidebarWidth',
        'sidebarOpenFilesVisible',
        'sidebarFilesOpenFraction',
        'sidebarSide',
        'uiDensity',
        'foldedProjects',
        'sidebarTab',
        'sidebarAgentStatus',
        'sidebarFilter',
        'sidebarWorktrees',
        'fontSize',
        'fontName',
        'uiFontSize',
        'uiFontName',
        'uiScheme',
        'theme',
        'grandmaMode',
        'cursor',
        'cursorGrayscale',
        'visualEffects',
    }
    result = {key: value for key, value in values.items() if key in allowed}
    # Display-only input for native abbreviated cwd titles; never a mounted path.
    result['hostHome'] = str(Path.home())
    return result


def write_preferences(root: Path, values: Mapping[str, Any]) -> None:
    """Publish the allowlisted snapshot atomically for concurrent local readers."""
    destination = root / 'assets/reference-appearance.json'
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_suffix('.json.partial')
    temporary.write_text(json.dumps(values))
    temporary.replace(destination)


def prepare_fonts(bundle: Path, game: Path, root: Path, values: Mapping[str, Any]) -> None:
    values = dict(values)
    fonts = root / 'assets/fonts'
    fonts.mkdir(parents=True, exist_ok=True)
    env = os.environ.copy()
    env['LD_LIBRARY_PATH'] = str(game) + (':' + env['LD_LIBRARY_PATH'] if env.get('LD_LIBRARY_PATH') else '')

    def probe(*args: object) -> None:
        subprocess.run(
            ['dotnet', str(bundle / 'font-probe/FontProbe.dll'), str(game), *(str(arg) for arg in args)],
            check=True,
            env=env,
        )

    probe('--catalog', bundle / 'mod/SlopWorld.dll', '', fonts)
    try:
        probe('--resolve-ui', bundle / 'mod/SlopWorld.dll', values.get('uiFontName') or '', fonts)
    except subprocess.CalledProcessError:
        if not values.get('uiFontName'):
            raise
        # A removed desktop font must not prevent opening browser settings.
        # Publish Auto with its bakes so startup does not retry the stale family;
        # the desktop profile and interactive font-request policy remain separate.
        probe('--resolve-ui', bundle / 'mod/SlopWorld.dll', '', fonts)
        values['uiFontName'] = ''
    probe('--resolve', bundle / 'mod/SlopWorld.dll', values.get('fontName') or 'monospace', fonts)
    # Missing preferences retain ModSettings' defaults. UI size zero explicitly
    # selects RimWorld's original 11/13/15 tiers instead.
    size = max(0, min(48, int(values.get('uiFontSize', 14)))) or 13
    # TerminalFont clamps even explicitly configured zero/out-of-range sizes.
    terminal_size = max(8, min(28, int(values.get('fontSize', 14))))
    manifest = []
    # Only seed the native UI indices and terminal metrics. Glyphs, styles,
    # other sizes and display densities are requested by the running workspace.
    for index, pixels in enumerate([max(1, size + offset) for offset in (-2, 0, 2)]):
        bake = f'ui-{pixels}.json'
        probe(fonts / 'ui.ttf', fonts / bake, pixels, 1, 0)
        manifest.append({'font': 'ui.ttf', 'bake': bake, 'index': index})
    probe(fonts / 'terminal.ttf', fonts / 'terminal.json', terminal_size, 1, 0)
    manifest.append({'font': 'terminal.ttf', 'bake': 'terminal.json'})
    (fonts / 'manifest.json').write_text(json.dumps(manifest))
    write_preferences(root, values)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('game', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    os.umask(0o077)
    prepare_fonts(Path(__file__).resolve().parent, args.game.resolve(), args.output.resolve(), preferences())
