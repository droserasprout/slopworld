"""Inspect native reference configuration on the host without exposing credentials."""

import json
import os
import shutil
import tomllib
import xml.etree.ElementTree as ET
from pathlib import Path


def main():
    home = Path.home()
    processes = []
    for proc in Path('/proc').iterdir():
        if not proc.name.isdecimal():
            continue
        try:
            executable = (proc / 'exe').resolve().name
            if executable not in ('RimWorld', 'RimWorldLinux', 'slopd'):
                continue
            environment = dict(
                item.split('=', 1) for item in (proc / 'environ').read_bytes().decode().split('\0') if '=' in item
            )
            processes.append(
                {
                    'pid': int(proc.name),
                    'executable': executable,
                    'environment': {
                        key: value
                        for key, value in environment.items()
                        if key
                        in (
                            'DISPLAY',
                            'WAYLAND_DISPLAY',
                            'SLOPWORLD_PROFILE',
                            'SLOPD_CONFIG_ROOT',
                            'SLOPD_DATA',
                            'SLOPD_CACHE',
                            'SLOPD_ENDPOINT',
                            'SLOPWORLD_GAME',
                        )
                    },
                }
            )
        except (OSError, ValueError):
            continue
    config = Path(os.environ.get('XDG_CONFIG_HOME', home / '.config')) / 'slopworld'
    data = Path(os.environ.get('XDG_DATA_HOME', home / '.local/share')) / 'slopworld'
    settings = data / 'profiles/local/Config/SlopWorld.toml'
    values = tomllib.loads(settings.read_text()) if settings.is_file() else {}
    prefs = settings.parent / 'Prefs.xml'
    display_preferences = {}
    if prefs.is_file():
        tree = ET.parse(prefs)
        for key in ('screenWidth', 'screenHeight', 'fullScreen', 'uiScale'):
            display_preferences[key] = tree.findtext('.//' + key)
    print(
        json.dumps(
            {
                'processes': processes,
                'hostEndpointExists': (config / 'endpoints/local.toml').is_file(),
                'displayPreferences': display_preferences,
                'appearance': {
                    key: value
                    for key, value in values.items()
                    if key
                    in (
                        'fontName',
                        'fontSize',
                        'uiFontName',
                        'uiFontSize',
                        'theme',
                        'uiDensity',
                        'sidebarWidth',
                        'sidebarAgentStatus',
                        'sidebarOpenFilesVisible',
                        'sidebarFilesOpenFraction',
                        'navigationSide',
                        'terminalTheme',
                    )
                },
                'profiles': [str(p) for p in (data / 'profiles').glob('*/Config/SlopWorld.toml')],
                'display': {key: os.environ.get(key) for key in ('DISPLAY', 'WAYLAND_DISPLAY')},
                'captureTools': {
                    name: shutil.which(name)
                    for name in (
                        'xwininfo',
                        'xrandr',
                        'import',
                        'grim',
                        'xdotool',
                        'spectacle',
                        'gnome-screenshot',
                        'gdbus',
                        'qdbus6',
                    )
                },
            },
            indent=2,
        )
    )


if __name__ == '__main__':
    main()
