"""Generate and install shell completions; CLI owners supply the command trees."""

import argparse
import os
import shutil
from pathlib import Path

from tools import ROOT
from tools.utils import run
from tools.utils import run_main

CLIENTS = ('slopctl', 'slopworld', 'slopcar')
SHELLS = ('bash', 'zsh', 'fish')


def relative_path(shell: str, binary: str) -> Path:
    return {
        'bash': Path('bash-completion/completions') / binary,
        'zsh': Path('zsh/site-functions') / f'_{binary}',
        'fish': Path('fish/vendor_completions.d') / f'{binary}.fish',
    }[shell]


def stage(source: Path, destination: Path, binaries: tuple[str, ...]) -> None:
    for binary in binaries:
        for shell in SHELLS:
            relative = relative_path(shell, binary)
            target = destination / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source / relative, target)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=('generate', 'install', 'uninstall'))
    parser.add_argument('binaries', choices=CLIENTS, nargs='+')
    args = parser.parse_args()
    source = ROOT / 'packaging/completions'
    if args.action == 'generate':
        for binary in args.binaries:
            executable = ROOT / os.environ['SLOPCAR'] if binary == 'slopcar' else ROOT / os.environ['TARGET'] / binary
            for shell in SHELLS:
                text = run([str(executable), 'completions', shell], capture_output=True, text=True).stdout
                target = source / relative_path(shell, binary)
                target.parent.mkdir(parents=True, exist_ok=True)
                if not target.exists() or target.read_text() != text:
                    target.write_text(text)
        return
    destination = Path(os.environ.get('XDG_DATA_HOME') or Path.home() / '.local/share')
    if args.action == 'install':
        stage(source, destination, tuple(args.binaries))
    else:
        for binary in args.binaries:
            for shell in SHELLS:
                (destination / relative_path(shell, binary)).unlink(missing_ok=True)


if __name__ == '__main__':
    run_main(main)
