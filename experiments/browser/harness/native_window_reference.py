"""Capture only the existing native game's X11 window, without focus or input."""

import json
import subprocess
import sys
from pathlib import Path


def main():
    games = []
    for proc in Path('/proc').iterdir():
        if not proc.name.isdecimal():
            continue
        try:
            if (proc / 'exe').resolve().name in ('RimWorld', 'RimWorldLinux'):
                games.append(proc)
        except OSError:
            continue
    if len(games) != 1:
        raise RuntimeError(f'Expected exactly one native game, found {len(games)}')
    game = games[0]
    windows = subprocess.check_output(['xdotool', 'search', '--pid', game.name], text=True).split()
    if len(windows) != 1:
        raise RuntimeError(f'Expected one game window, found {len(windows)}')
    window = windows[0]
    pid = subprocess.check_output(['xdotool', 'getwindowpid', window], text=True).strip()
    if pid != game.name:
        raise RuntimeError('Game window ownership changed before capture')
    geometry = subprocess.check_output(['xdotool', 'getwindowgeometry', '--shell', window], text=True)
    destination = Path(sys.argv[1]).resolve()
    destination.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run(['import', '-window', window, str(destination)], check=True, timeout=20)
    evidence = {'pid': int(pid), 'window': window, 'geometry': geometry, 'path': str(destination)}
    destination.with_suffix('.json').write_text(json.dumps(evidence, indent=2))
    print(json.dumps(evidence))


if __name__ == '__main__':
    main()
