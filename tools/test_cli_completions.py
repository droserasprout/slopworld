"""Protect completion installation and command selection in an actual shell."""

import os
import pty
import select
import shlex
import shutil
import subprocess
from pathlib import Path

import pytest

from tools import ROOT
from tools import cli_completions


def test_install_and_uninstall_preserve_other_clients(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    data = tmp_path / 'data'
    monkeypatch.setenv('XDG_DATA_HOME', str(data))
    monkeypatch.setattr('sys.argv', ['completions', 'install', 'slopctl', 'slopworld'])
    cli_completions.main()
    unrelated = data / 'zsh/site-functions/_unrelated'
    unrelated.write_text('keep')
    for shell in cli_completions.SHELLS:
        path = cli_completions.relative_path(shell, 'slopctl')
        assert (data / path).read_bytes() == (ROOT / 'packaging/completions' / path).read_bytes()
    monkeypatch.setattr('sys.argv', ['completions', 'uninstall', 'slopctl'])
    cli_completions.main()
    cli_completions.main()
    for shell in cli_completions.SHELLS:
        assert not (data / cli_completions.relative_path(shell, 'slopctl')).exists()
        assert (data / cli_completions.relative_path(shell, 'slopworld')).exists()
    assert unrelated.read_text() == 'keep'


@pytest.mark.skipif(shutil.which('zsh') is None, reason='zsh is required for completion behavior')
def test_zsh_completes_launcher_commands_and_values(tmp_path: Path) -> None:
    master, slave = pty.openpty()
    reader, writer = os.pipe()
    completions = ROOT / 'packaging/completions/zsh/site-functions'
    # A separate pipe acknowledges shell readiness and each completed buffer;
    # the test never executes the launcher or depends on terminal repaint timing.
    (tmp_path / '.zshrc').write_text(
        f'fpath=({shlex.quote(str(completions))} $fpath)\n'
        'autoload -Uz compinit\n'
        'compinit -D\n'
        "PROMPT='' RPROMPT=''\n"
        'bindkey -e\n'
        f'capture_completion() {{ zle complete-word; print -r -- "$BUFFER" >&{writer}; }}\n'
        'zle -N capture_completion\n'
        "bindkey '^I' capture_completion\n"
        f'print -r -- ready >&{writer}\n'
    )

    def response() -> str:
        assert select.select([reader], [], [], 10)[0], 'Zsh completion did not respond'
        return os.read(reader, 4096).decode().rstrip('\n')

    try:
        with subprocess.Popen(
            ['zsh', '-d', '-i'],
            stdin=slave,
            stdout=slave,
            stderr=slave,
            cwd=tmp_path,
            env={**os.environ, 'ZDOTDIR': str(tmp_path), 'TERM': 'xterm'},
            pass_fds=(writer,),
            start_new_session=True,
        ) as shell:
            try:
                assert response() == 'ready'
                for before, after in [
                    ('slopworld pat', 'slopworld paths '),
                    ('slopworld mod ins', 'slopworld mod install '),
                    ('slopworld profile ini', 'slopworld profile init '),
                    ('slopworld profile res', 'slopworld profile reset '),
                    ('slopworld paths pro', 'slopworld paths profile '),
                    ('slopworld --sidecar mod ins', 'slopworld --sidecar mod install '),
                    ('slopworld --endpoint remote pat', 'slopworld --endpoint remote paths '),
                ]:
                    os.write(master, f'\x15{before}\t'.encode())
                    assert response() == after
            finally:
                shell.kill()
    finally:
        for descriptor in (master, slave, reader, writer):
            os.close(descriptor)
