"""Keep browser launch discovery aligned with native host paths and local commands."""

import importlib
import os
import subprocess
import sys
from pathlib import Path
from typing import Any

import pytest

from tools import ROOT


@pytest.fixture
def paths(monkeypatch: pytest.MonkeyPatch) -> Any:
    monkeypatch.syspath_prepend(str(ROOT / 'slopweb'))
    return importlib.import_module('application_paths')


def test_shared_roots_ignore_relative_xdg_and_expand_overrides(paths: Any, tmp_path: Path) -> None:
    env = {'HOME': str(tmp_path), 'XDG_DATA_HOME': 'relative', 'XDG_CACHE_HOME': '~/cache'}
    result = paths.resolve(env)
    assert result.profile == tmp_path / '.local/share/slopworld/profiles/local'
    assert result.web_cache == tmp_path / '.cache/slopweb'
    env.update(SLOPD_DATA='~/private', SLOPD_CACHE='~/shared-cache', SLOPD_CONFIG_ROOT='~/config')
    result = paths.resolve(env, endpoint='remote', sidecar=True)
    assert result.profile == tmp_path / 'private/sidecar/profiles/remote'
    assert result.data == tmp_path / 'private/sidecar/daemon'
    assert result.cache == tmp_path / 'shared-cache/sidecar'
    assert result.endpoint == tmp_path / 'config/sidecar/endpoints/remote.toml'
    assert result.web_cache == tmp_path / '.cache/slopweb'
    assert paths.profile_path(env, profile='~/custom') == tmp_path / 'custom'


def test_profile_follows_descriptor_and_explicit_name(paths: Any, tmp_path: Path) -> None:
    env = {'HOME': str(tmp_path), 'SLOPD_ENDPOINT': '~/peers/other.toml'}
    assert paths.profile_path(env).name == 'other'
    assert paths.profile_path(env, endpoint='selected').name == 'selected'
    env['SLOPWORLD_PROFILE'] = '~/fixed'
    assert paths.profile_path(env, endpoint='selected') == tmp_path / 'fixed'
    with pytest.raises(ValueError, match='endpoint name'):
        paths.endpoint_path(env, '../escape')


def test_game_discovery_precedence_and_invalid_saved_default(paths: Any, tmp_path: Path) -> None:
    env = {'HOME': str(tmp_path)}
    guessed = tmp_path / 'RimWorld/game'
    saved = tmp_path / 'saved'
    explicit = tmp_path / 'explicit'
    for game in (guessed, saved, explicit):
        game.mkdir(parents=True)
        (game / 'RimWorld').touch()
    assert paths.game_directory(env=env) == guessed
    config = tmp_path / '.config/slopworld/game.toml'
    config.parent.mkdir(parents=True)
    config.write_text('path = "~/saved"\n')
    assert paths.game_directory(env=env) == saved
    env['SLOPWORLD_GAME'] = ' \t'
    assert paths.game_directory(env=env) == saved
    env['SLOPWORLD_GAME'] = '~/explicit'
    assert paths.game_directory(env=env) == explicit
    config.write_text('path = "~/missing"\n')
    assert paths.game_directory(explicit, env) == explicit
    env.pop('SLOPWORLD_GAME')
    with pytest.raises(ValueError, match='saved in'):
        paths.game_directory(env=env)
    config.write_text('invalid = true\n')
    with pytest.raises(ValueError, match='Invalid game path'):
        paths.game_directory(env=env)
    config.unlink()
    config.symlink_to(tmp_path / 'missing-config')
    with pytest.raises(FileNotFoundError):
        paths.game_directory(env=env)


def test_local_commands_do_not_prepare_game_or_sdk(tmp_path: Path) -> None:
    env = {key: value for key, value in os.environ.items() if not key.startswith(('SLOPD_', 'SLOPWORLD_'))}
    env.update(HOME=str(tmp_path), XDG_DATA_HOME='relative', XDG_CONFIG_HOME='relative')
    launcher = ROOT / 'slopweb/launch.py'
    for args, expected in [
        (['--endpoint', 'remote', 'paths', 'profile'], str(tmp_path / '.local/share/slopworld/profiles/remote')),
        (['paths', 'web-cache', '--cache', '~/private'], str(tmp_path / 'private')),
        (['paths', 'profile', '--profile', '~/profile'], str(tmp_path / 'profile')),
    ]:
        result = subprocess.run(
            [sys.executable, str(launcher), *args], env=env, capture_output=True, text=True, check=True
        )
        assert result.stdout.strip() == expected
    assert not list(tmp_path.iterdir())
    result = subprocess.run(
        [sys.executable, str(launcher), '--version'], env=env, capture_output=True, text=True, check=True
    )
    assert result.stdout.startswith('slopweb ')


@pytest.mark.parametrize('shell', ['bash', 'zsh', 'fish'])
def test_packaged_completions_match_parser(paths: Any, shell: str) -> None:
    cli = importlib.import_module('cli')
    from tools.cli_completions import relative_path

    assert (ROOT / 'packaging/completions' / relative_path(shell, 'slopweb')).read_text() == cli.completion_script(
        shell
    )


def test_bash_completion_handles_global_options_and_path_values(paths: Any, tmp_path: Path) -> None:
    import shlex

    script = ROOT / 'packaging/completions/bash-completion/completions/slopweb'
    directory = tmp_path / 'fixture-dir with spaces'
    directory.mkdir()
    token = tmp_path / 'fixture-token'
    token.touch()
    for words, expected in [
        (['slopweb', '--endpoint', 'remote', 'paths', 'pro'], 'profile'),
        (['slopweb', '--game', str(tmp_path / 'fixture-d')], str(directory)),
        (['slopweb', '--token-file', str(tmp_path / 'fixture-t')], str(token)),
    ]:
        command = (
            f'source {shlex.quote(str(script))}\n'
            f'COMP_WORDS=({shlex.join(words)})\n'
            f'COMP_CWORD={len(words) - 1}\n'
            '_slopweb\nprintf "%s\\n" "${COMPREPLY[@]}"\n'
        )
        result = subprocess.run(['bash', '-c', command], capture_output=True, text=True, check=True)
        assert result.stdout.strip() == expected


@pytest.mark.parametrize('arguments', [['paths', 'profile'], ['completions', 'bash'], ['--version'], ['-V']])
def test_just_local_commands_skip_sdk_provisioning(
    paths: Any, monkeypatch: pytest.MonkeyPatch, arguments: list[str]
) -> None:
    wrapper = importlib.import_module('just_launch')
    monkeypatch.setattr(wrapper.aot, 'toolchain', lambda *args: pytest.fail('Local commands must not query an SDK'))
    monkeypatch.setattr(wrapper.shutil, 'which', lambda *args: pytest.fail('Local commands must not discover an SDK'))
    wrapper.ensure_workload(arguments)


@pytest.mark.parametrize(
    'origin',
    [
        'http://example.com',
        'https://example.com/path',
        'https://user@example.com',
        'https://example.com?query',
        'https://example.com#fragment',
    ],
)
def test_public_origin_rejects_non_origin_urls(paths: Any, origin: str) -> None:
    cli = importlib.import_module('cli')
    with pytest.raises(SystemExit):
        cli.parser().parse_args(['--public-origin', origin])


@pytest.mark.parametrize('arguments', [[], ['--interpreted']])
def test_interpreted_launch_skips_workload_provisioning(
    paths: Any, monkeypatch: pytest.MonkeyPatch, arguments: list[str]
) -> None:
    wrapper = importlib.import_module('just_launch')
    monkeypatch.delenv('SLOPWEB_AOT_SDK', raising=False)
    if arguments:
        monkeypatch.setenv('SLOPWEB_AOT_SDK', '/unused/sdk')
    monkeypatch.setattr(wrapper.aot, 'toolchain', lambda _: pytest.fail('Interpreter must not provision tools'))
    wrapper.ensure_workload(arguments)


@pytest.mark.parametrize('arguments', [['--aot'], ['--aot-sdk', '/sdk/dotnet']])
def test_explicit_aot_selects_toolchain(paths: Any, monkeypatch: pytest.MonkeyPatch, arguments: list[str]) -> None:
    wrapper = importlib.import_module('just_launch')
    monkeypatch.delenv('SLOPWEB_AOT_SDK', raising=False)
    monkeypatch.setattr(wrapper.shutil, 'which', lambda _: '/sdk/dotnet')
    selected = []
    monkeypatch.setattr(wrapper.aot, 'toolchain', lambda sdk: selected.append(sdk))
    wrapper.ensure_workload(arguments)
    assert selected == [Path('/sdk/dotnet')]
