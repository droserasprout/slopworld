"""Prepare host presentation inputs in the private runtime, never the package."""

import argparse
import json
import os
import subprocess
import tomllib
from collections.abc import Mapping
from pathlib import Path
from typing import Any

from application_paths import profile_path

# Keep this boundary aligned with BrowserPreferences.Allowed; the contract test
# checks both copies so installed Python never needs repository C# sources.
PRESENTATION_PREFERENCES = frozenset(
    {
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
        'sidebarShowHidden',
        'sidebarShowGitignored',
        'cursorColor',
        'smoothScrolling',
        'statusbarUsage',
        'statusbarSummaryPosition',
        'statusbarClockPosition',
        'statusbarPlanner',
        'statusbarJukebox',
        'statusbarGM',
        'statusbarAgentIndicators',
        'timeZone',
        'timeFormat',
        'dateFormat',
        'ecoDim',
    }
)


def preferences(profile: Path | None = None) -> dict[str, Any]:
    """Import presentation from an explicitly selected or default profile directory."""
    profile = profile if profile is not None else profile_path(os.environ)
    settings = profile / 'Config/SlopWorld.toml'
    values = tomllib.loads(settings.read_text()) if settings.is_file() else {}
    result = {key: value for key, value in values.items() if key in PRESENTATION_PREFERENCES}
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
        config = json.loads((fonts / 'ui-config.json').read_text())
        (fonts / bake).write_text(json.dumps({**config, 'size': pixels}))
        manifest.append({'font': 'ui.ttf', 'bake': bake, 'index': index})
    config = json.loads((fonts / 'terminal-config.json').read_text())
    (fonts / 'terminal.json').write_text(json.dumps({**config, 'size': terminal_size}))
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
