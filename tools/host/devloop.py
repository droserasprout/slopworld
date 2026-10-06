"""Select a Git worktree and reinstall/relaunch while preserving each checkout's build cache."""

import os
import re
import signal
from dataclasses import dataclass
from pathlib import Path
from types import FrameType

from tools.utils import log
from tools.utils import run
from tools.utils import run_main


@dataclass(frozen=True)
class Worktree:
    path: Path
    label: str = 'detached'


def parse_worktrees(porcelain: bytes) -> list[Worktree]:
    worktrees = []
    for field in os.fsdecode(porcelain).split('\0'):
        if field.startswith('worktree '):
            worktrees.append(Worktree(Path(field.removeprefix('worktree '))))
        elif field.startswith('branch ') and worktrees:
            worktrees[-1] = Worktree(worktrees[-1].path, field.removeprefix('branch refs/heads/'))
    return worktrees


def select(choice: str, worktrees: list[Worktree], current: Path) -> Path:
    if not choice:
        return current
    if (
        len(choice) <= len(str(len(worktrees)))
        and re.fullmatch('[1-9][0-9]*', choice)
        and int(choice) <= len(worktrees)
    ):
        return worktrees[int(choice) - 1].path
    raise ValueError('Choose a listed number.')


def main() -> None:
    just = os.environ['JUST_CMD']
    repository = run(
        ['git', 'rev-parse', '--path-format=absolute', '--git-common-dir'],
        cwd=Path.cwd(),
        capture_output=True,
        text=True,
    ).stdout.strip()
    current = Path.cwd()
    signal.signal(signal.SIGTERM, exit_on_signal)
    try:
        terminal_input = open('/dev/tty')
        terminal_output = open('/dev/tty', 'w')
    except OSError as error:
        raise ValueError('devloop requires an interactive terminal') from error
    with terminal_input, terminal_output:
        while True:
            worktrees = parse_worktrees(
                run(
                    ['git', f'--git-dir={repository}', 'worktree', 'list', '--porcelain', '-z'], capture_output=True
                ).stdout
            )
            if not worktrees:
                raise ValueError('No worktrees available.')
            for index, worktree in enumerate(worktrees, 1):
                log(f'{index}) {worktree.label}  {worktree.path}', file=terminal_output)
            print(f'Enter: {current}; number: switch; q: quit > ', end='', file=terminal_output, flush=True)
            line = terminal_input.readline()
            if not line or line.rstrip('\n') == 'q':
                return
            try:
                current = select(line.rstrip('\n'), worktrees, current)
            except ValueError as error:
                log(str(error), file=terminal_output)
                continue
            if not current.is_dir():
                log(f'Checkout unavailable: {current}', file=terminal_output)
                continue
            commit = run(
                ['git', '-C', str(current), 'rev-parse', '--short', 'HEAD'], capture_output=True, text=True, check=False
            )
            if commit.returncode:
                log(f'Checkout unavailable: {current}', file=terminal_output)
                continue
            log(f'\nBuilding {current} ({commit.stdout.strip()})', file=terminal_output)
            # One invocation shares install/run dependencies; a failure returns to selection.
            run([just, 'install', 'run'], cwd=current, check=False)


def exit_on_signal(_signal: int, _frame: FrameType | None) -> None:
    raise SystemExit(143)


if __name__ == '__main__':
    run_main(main)
