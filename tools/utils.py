"""Interpret just's command overrides and report tool failures without shell execution."""

import os
import shlex
import subprocess
import sys
from collections.abc import Callable
from pathlib import Path
from typing import Any
from typing import Literal
from typing import TextIO
from typing import overload

from tools import ROOT


def command(setting: str, default: str) -> list[str]:
    arguments = shlex.split(os.environ.get(setting, default))
    if not arguments:
        raise ValueError(f'{setting} must name a command')
    return arguments


def log(message: str, *, file: TextIO | None = None) -> None:
    print(message, file=file, flush=True)


@overload
def run(
    arguments: list[str], *, cwd: Path = ROOT, check: bool = True, text: Literal[True], **kwargs: Any
) -> subprocess.CompletedProcess[str]: ...


@overload
def run(
    arguments: list[str], *, cwd: Path = ROOT, check: bool = True, text: Literal[False] = False, **kwargs: Any
) -> subprocess.CompletedProcess[bytes]: ...


def run(
    arguments: list[str], *, cwd: Path = ROOT, check: bool = True, text: bool = False, **kwargs: Any
) -> subprocess.CompletedProcess[Any]:
    # subprocess owns the keyword surface; text mode determines the captured output type.
    if text:
        return subprocess.run(arguments, cwd=cwd, check=check, text=True, **kwargs)
    return subprocess.run(arguments, cwd=cwd, check=check, **kwargs)


@overload
def spawn(arguments: list[str], *, cwd: Path = ROOT, text: Literal[True], **kwargs: Any) -> subprocess.Popen[str]: ...


@overload
def spawn(
    arguments: list[str], *, cwd: Path = ROOT, text: Literal[False] = False, **kwargs: Any
) -> subprocess.Popen[bytes]: ...


def spawn(arguments: list[str], *, cwd: Path = ROOT, text: bool = False, **kwargs: Any) -> subprocess.Popen[Any]:
    if text:
        return subprocess.Popen(arguments, cwd=cwd, text=True, **kwargs)
    return subprocess.Popen(arguments, cwd=cwd, **kwargs)


def run_main(main: Callable[[], None]) -> None:
    try:
        main()
    except subprocess.CalledProcessError as error:
        # Arguments may contain a GOG authorization code. Report only the status.
        log(f'command failed with exit status {error.returncode}', file=sys.stderr)
        raise SystemExit(error.returncode if error.returncode > 0 else 128 - error.returncode) from error
    except (OSError, ValueError) as error:
        log(f'error: {error}', file=sys.stderr)
        raise SystemExit(1) from error
    except KeyError as error:
        log(f'error: missing setting {error.args[0]}', file=sys.stderr)
        raise SystemExit(1) from error
    except KeyboardInterrupt:
        raise SystemExit(130) from None
