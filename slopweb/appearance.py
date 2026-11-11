"""Prepare host presentation inputs in the private runtime, never the package."""

import argparse
import json
import os
import subprocess
import tomllib
from pathlib import Path


def preferences() -> dict:
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


def write_preferences(root: Path, values: dict) -> None:
    """Publish the allowlisted snapshot atomically for concurrent local readers."""
    destination = root / 'assets/reference-appearance.json'
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_suffix('.json.partial')
    temporary.write_text(json.dumps(values))
    temporary.replace(destination)


def prepare_fonts(bundle: Path, game: Path, root: Path, values: dict) -> None:
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

    probe('--resolve-ui', bundle / 'mod/SlopWorld.dll', values.get('uiFontName') or '', fonts)
    probe('--resolve', bundle / 'mod/SlopWorld.dll', values.get('fontName') or 'monospace', fonts)
    # Missing preferences retain ModSettings' defaults. UI size zero explicitly
    # selects RimWorld's original 11/13/15 tiers instead.
    size = max(1, min(48, int(values.get('uiFontSize', 14) or 13)))
    manifest = []
    # Keep common desktop densities local. The runtime switches atlas/metrics
    # together on viewport changes; other densities use the nearest native bake.
    for density in (1, 5 / 3, 2):
        suffix = f'{density:.6g}'
        for style in (0, 1, 2, 3):
            for index, offset in enumerate((-2, 0, 2)):
                pixels = max(1, size + offset)
                bake = f'ui-{pixels}-style{style}@{suffix}.json'
                probe(fonts / 'ui.ttf', fonts / bake, pixels, density, style)
                manifest.append({'font': 'ui.ttf', 'bake': bake, 'index': index})
            bake = f'terminal-style{style}@{suffix}.json'
            probe(fonts / 'terminal.ttf', fonts / bake, values.get('fontSize', 14) or 13, density, style)
            manifest.append({'font': 'terminal.ttf', 'bake': bake})
    (fonts / 'manifest.json').write_text(json.dumps(manifest))
    write_preferences(root, values)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('game', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    os.umask(0o077)
    prepare_fonts(Path(__file__).resolve().parent, args.game.resolve(), args.output.resolve(), preferences())
