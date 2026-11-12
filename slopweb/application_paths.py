"""Resolve shared host paths and game discovery; browser runtime caches stay separate."""

import os
import re
import tomllib
from collections.abc import Mapping
from dataclasses import dataclass
from pathlib import Path

PATH_NAMES = ('config', 'data', 'cache', 'endpoint', 'profile', 'game-log', 'web-cache', 'game')


def expand(path: str | Path, env: Mapping[str, str]) -> Path:
    value = Path(path)
    if value.parts and value.parts[0] == '~':
        value = Path(env.get('HOME') or Path.home()).joinpath(*value.parts[1:])
    return value.absolute()


def root(name: str, env: Mapping[str, str]) -> Path:
    variable, xdg, fallback = {
        'config': ('SLOPD_CONFIG_ROOT', 'XDG_CONFIG_HOME', '.config'),
        'data': ('SLOPD_DATA', 'XDG_DATA_HOME', '.local/share'),
        'cache': ('SLOPD_CACHE', 'XDG_CACHE_HOME', '.cache'),
    }[name]
    if variable in env:
        return expand(env[variable], env)
    base = Path(env.get(xdg, ''))
    if not base.is_absolute():
        base = Path(env.get('HOME') or Path.home()) / fallback
    return (base / 'slopworld').absolute()


def endpoint_path(env: Mapping[str, str], endpoint: str | None = None, sidecar: bool = False) -> Path:
    config = root('config', env) / 'sidecar' if sidecar else root('config', env)
    if endpoint is not None:
        if not re.fullmatch(r'[A-Za-z0-9_-]+', endpoint):
            raise ValueError("endpoint name must contain only letters, digits, '-' or '_'")
        return config / 'endpoints' / (endpoint + '.toml')
    return expand(env['SLOPD_ENDPOINT'], env) if 'SLOPD_ENDPOINT' in env else config / 'endpoints/local.toml'


def profile_path(
    env: Mapping[str, str], endpoint: str | None = None, profile: str | Path | None = None, sidecar: bool = False
) -> Path:
    selected = profile if profile is not None else env.get('SLOPWORLD_PROFILE', '')
    if str(selected).strip():
        return expand(selected, env)
    data = root('data', env) / 'sidecar' if sidecar else root('data', env)
    return data / 'profiles' / endpoint_path(env, endpoint, sidecar).stem


def browser_cache(env: Mapping[str, str]) -> Path:
    base = Path(env.get('XDG_CACHE_HOME', ''))
    if not base.is_absolute():
        base = Path(env.get('HOME') or Path.home()) / '.cache'
    return (base / 'slopweb').absolute()


@dataclass(frozen=True)
class Paths:
    config: Path
    data: Path
    cache: Path
    endpoint: Path
    profile: Path
    game_log: Path
    web_cache: Path

    def query(self, name: str) -> Path:
        return {
            'config': self.config,
            'data': self.data,
            'cache': self.cache,
            'endpoint': self.endpoint,
            'profile': self.profile,
            'game-log': self.game_log,
            'web-cache': self.web_cache,
        }[name]


def resolve(
    env: Mapping[str, str] | None = None,
    endpoint: str | None = None,
    profile: str | Path | None = None,
    sidecar: bool = False,
) -> Paths:
    env = os.environ if env is None else env
    config, data, cache = (root(name, env) for name in ('config', 'data', 'cache'))
    if sidecar:
        config, data, cache = config / 'sidecar', data / 'sidecar/daemon', cache / 'sidecar'
    log = env.get('SLOPWORLD_GAME_LOG', '')
    return Paths(
        config,
        data,
        cache,
        endpoint_path(env, endpoint, sidecar),
        profile_path(env, endpoint, profile, sidecar),
        expand(log, env) if log.strip() else cache / 'Player.log',
        browser_cache(env),
    )


def game_directory(explicit: str | Path | None = None, env: Mapping[str, str] | None = None) -> Path:
    env = os.environ if env is None else env

    def validate(value: str | Path) -> Path:
        path = expand(value, env)
        if not (path / 'RimWorld').is_file():
            raise ValueError(f'no RimWorld in {path}')
        return path

    override = (
        explicit
        if explicit is not None
        else (env.get('SLOPWORLD_GAME') if env.get('SLOPWORLD_GAME', '').strip() else None)
    )
    if override is not None:
        return validate(override)
    config = root('config', env) / 'game.toml'
    # lexists keeps a dangling saved configuration from silently falling back.
    if os.path.lexists(config):
        values = tomllib.loads(config.read_text())
        if set(values) != {'path'} or not isinstance(values['path'], str) or not values['path'].strip():
            raise ValueError(f'Invalid game path in {config}')
        try:
            return validate(values['path'])
        except ValueError as error:
            raise ValueError(f'{error} (saved in {config}); override with --game DIR or SLOPWORLD_GAME') from error
    for guess in (
        '~/.local/share/Steam/steamapps/common/RimWorld',
        '~/.steam/steam/steamapps/common/RimWorld',
        '~/RimWorld/game',
        '~/GOG Games/RimWorld/game',
    ):
        path = expand(guess, env)
        if (path / 'RimWorld').is_file():
            return path
    raise ValueError('slopweb could not find RimWorld. Pass --game DIR or set SLOPWORLD_GAME.')
