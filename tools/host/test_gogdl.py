"""GOG setup validates local state before invoking external commands."""

import io
import sys
from unittest.mock import patch

import pytest

from tools.host import gogdl


def test_empty_code_does_not_call_gogdl(tmp_path, monkeypatch):
    monkeypatch.setenv('GOGDL_LOGIN_URL', 'https://example.test/login')
    with (
        patch.object(gogdl.shutil, 'which', return_value=None),
        patch.object(sys, 'stdin', io.StringIO('\n')),
        patch.object(gogdl, 'run') as run,
    ):
        with pytest.raises(ValueError, match='authorization code is empty'):
            gogdl.login(tmp_path / 'auth.json', ['gogdl'])
        run.assert_not_called()


def test_login_requires_saved_credentials(tmp_path, monkeypatch):
    monkeypatch.setenv('GOGDL_LOGIN_URL', 'https://example.test/login')
    auth = tmp_path / 'auth.json'
    for saved in (False, True):

        def authenticate(*args, **kwargs):
            if saved:
                auth.write_text('{}')

        with (
            patch.object(gogdl.shutil, 'which', return_value=None),
            patch.object(sys, 'stdin', io.StringIO('code with spaces\n')),
            patch.object(gogdl, 'run', side_effect=authenticate) as run,
        ):
            if saved:
                gogdl.login(auth, ['gogdl'])
            else:
                with pytest.raises(ValueError, match='did not save credentials'):
                    gogdl.login(auth, ['gogdl'])
            assert run.call_args.args[0] == ['gogdl', 'auth', '--code', 'code with spaces']


@pytest.mark.parametrize('action', ['install', 'update'])
def test_missing_credentials_prevent_game_changes(tmp_path, monkeypatch, action):
    monkeypatch.setenv('GOGDL_AUTH', str(tmp_path / 'auth.json'))
    monkeypatch.setenv('GOGDL_PATH', str(tmp_path / 'game'))
    monkeypatch.setenv('RIMWORLD', str(tmp_path / 'game'))
    (tmp_path / 'game').mkdir()
    game = tmp_path / 'game/RimWorldLinux'
    game.touch()
    game.chmod(0o755)
    with patch.object(sys, 'argv', ['gogdl', action]), patch.object(gogdl, 'run') as run:
        with pytest.raises(ValueError, match='gogdl login is missing'):
            gogdl.main()
        run.assert_not_called()


def test_install_keeps_paths_and_command_override_arguments(tmp_path, monkeypatch):
    auth = tmp_path / 'auth with spaces.json'
    auth.write_text('{}')
    game = tmp_path / 'game with spaces'
    for key, value in {
        'GOGDL_AUTH': str(auth),
        'GOGDL_PATH': str(game),
        'GOGDL_ID': '123',
        'GOGDL': '"/tmp/gogdl tool" --verbose',
    }.items():
        monkeypatch.setenv(key, value)
    with patch.object(sys, 'argv', ['gogdl', 'install']), patch.object(gogdl, 'run') as run:
        gogdl.main()
    assert run.call_args.args[0] == [
        '/tmp/gogdl tool',
        '--verbose',
        '--auth-config-path',
        str(auth),
        'download',
        '123',
        '--path',
        str(game),
        '--platform',
        'linux',
        '--with-dlcs',
    ]
