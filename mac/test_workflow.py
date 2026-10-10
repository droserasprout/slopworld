"""Native workflow reuses existing containers and fails before launching on invalid hosts."""

import subprocess
from unittest.mock import patch

import pytest

from mac import workflow


@pytest.mark.parametrize(
    'running,exists,expected', [(True, True, None), (False, True, []), (False, False, ['--workspace', '/tmp/work'])]
)
def test_start_preserves_existing_container_mounts(monkeypatch, running, exists, expected):
    monkeypatch.setenv('SLOPCAR_CONTAINER', 'test-container')
    monkeypatch.setenv('SLOPCAR', '/tmp/sidecar binary')
    responses = [
        subprocess.CompletedProcess([], 0 if exists else 1, stdout='true' if running else 'false'),
        subprocess.CompletedProcess([], 0 if exists else 1),
        subprocess.CompletedProcess([], 0),
    ]
    with patch.object(workflow, 'run', side_effect=responses) as run:
        workflow.start(['--workspace', '/tmp/work'])
    if expected is None:
        assert run.call_count == 1
    else:
        assert run.call_args.args[0] == ['/tmp/sidecar binary', 'start', *expected]


def test_game_check_rejects_non_macos_before_external_commands():
    with patch.object(workflow.platform, 'system', return_value='Linux'), patch.object(workflow, 'run') as run:
        with pytest.raises(ValueError, match='requires Darwin'):
            workflow.check('game-check')
        run.assert_not_called()


def test_game_check_supports_native_paths_with_spaces(tmp_path, monkeypatch):
    game = tmp_path / 'RimWorld by Ludeon Studios'
    game.touch()
    game.chmod(0o755)
    managed = tmp_path / 'Managed'
    managed.mkdir()
    (managed / 'Assembly-CSharp.dll').touch()
    mods = tmp_path / 'Mods'
    mods.mkdir()
    for key, value in {'MAC_GAME': str(game), 'MAC_MANAGED': str(managed), 'MAC_MODS': str(mods)}.items():
        monkeypatch.setenv(key, value)
    with (
        patch.object(workflow.platform, 'system', return_value='Darwin'),
        patch.object(workflow.shutil, 'which', return_value='/bin/dotnet'),
    ):
        workflow.check('game-check')
        (managed / 'Assembly-CSharp.dll').unlink()
        with pytest.raises(ValueError, match='missing RimWorld assemblies'):
            workflow.check('game-check')
