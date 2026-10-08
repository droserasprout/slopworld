"""Print required and optional host dependencies; fail only for missing requirements."""

import importlib.util
import os
import shutil
import subprocess
from pathlib import Path


def report(label: str, found: str | None, *, required: bool = True) -> int:
    print(f'  {label}: {found or "missing"}')
    return int(required and not found)


def commands(label: str, *names: str, required: bool = True) -> int:
    found = next((path for name in names if (path := shutil.which(name))), None)
    return report(f'{label} ({", ".join(names)})', found, required=required)


def main() -> int:
    missing = 0
    game = Path(os.environ['RIMWORLD']) if os.environ.get('RIMWORLD') else None

    print('Required: build dependencies')
    missing += commands('Python environment manager', 'uv')
    missing += commands('recipe runner', 'just')
    missing += commands('Rust/Cargo', 'cargo')
    missing += commands('.NET SDK', 'dotnet')
    path = game / 'RimWorldLinux_Data/Managed/Assembly-CSharp.dll' if game else None
    missing += report('RimWorld reference assemblies', str(path) if path and path.is_file() else None)

    print('Required: runtime dependencies')
    path = game / 'RimWorldLinux' if game else None
    missing += report(
        'native RimWorld executable', str(path) if path and path.is_file() and os.access(path, os.X_OK) else None
    )
    missing += commands('session multiplexer', 'tmux')
    missing += commands('sandbox isolation', 'bwrap')
    missing += commands('private networking', 'pasta')
    missing += commands('user service control', 'systemctl')
    missing += commands('user scopes', 'systemd-run')
    missing += commands('game process lookup', 'pgrep')
    missing += commands('workspace search', 'rg')
    missing += commands('Git view', 'git')
    missing += commands('default shell', 'bash')
    missing += commands('default pager', 'less')
    missing += commands('pager syntax highlighting', 'highlight')
    missing += commands('default editor', 'micro')
    missing += commands('desktop application associations', 'gio')
    missing += commands('native application chooser', 'gdbus')
    library = next(
        (
            str(Path(root) / 'libasound.so.2')
            for root in ('/usr/lib', '/usr/lib64')
            if (Path(root) / 'libasound.so.2').is_file()
        ),
        None,
    )
    if library is None and shutil.which('ldconfig'):
        cache = subprocess.run(['ldconfig', '-p'], capture_output=True, text=True, check=False).stdout
        library = next(
            (
                line.rsplit(' => ', 1)[1]
                for line in cache.splitlines()
                if line.split() and line.split()[0] == 'libasound.so.2' and ' => ' in line
            ),
            None,
        )
    missing += report('daemon audio backend (libasound.so.2)', library)
    missing += commands('agent CLI (one of)', 'claude', 'codex', 'opencode', 'pi')

    print('Optional: integrations')
    commands('Rust compiler cache', 'sccache', required=False)
    commands('Wayland clipboard copy', 'wl-copy', required=False)
    commands('Wayland clipboard paste', 'wl-paste', required=False)
    commands('X11 clipboard', 'xclip', 'xsel', required=False)

    print('Optional: developer tools')
    commands('redeploy helper', 'curl', required=False)
    commands('Mono IPC benchmark', 'mono', required=False)
    commands('human docs', 'mdbook', required=False)
    commands('game screenshot window lookup', 'xdotool', required=False)
    commands('game screenshot capture', 'import', required=False)
    commands('Python tooling', 'python3', required=False)
    module = importlib.util.find_spec('numpy')
    report('Python image module: numpy', (module.origin or 'numpy') if module else None, required=False)
    module = importlib.util.find_spec('PIL')
    report('Python image module: Pillow', (module.origin or 'PIL') if module else None, required=False)
    module = importlib.util.find_spec('cairo')
    report('Python Pango module: Pycairo', (module.origin or 'cairo') if module else None, required=False)
    module = importlib.util.find_spec('gi')
    report('Python Pango module: PyGObject', (module.origin or 'gi') if module else None, required=False)
    commands('SVG rasterizer', 'rsvg-convert', required=False)
    module = importlib.util.find_spec('cairosvg')
    report('SVG rasterizer fallback', (module.origin or 'cairosvg') if module else None, required=False)

    print(f'\n{missing} required dependencies missing.' if missing else '\nAll required dependencies found.')
    return int(missing > 0)


if __name__ == '__main__':
    raise SystemExit(main())
