"""Guard the shared host/browser presentation boundary and selected profile import."""

import importlib
import json
import re
from pathlib import Path
from typing import Any

import pytest

from tools import ROOT


@pytest.fixture
def appearance(monkeypatch: pytest.MonkeyPatch) -> Any:
    monkeypatch.syspath_prepend(str(ROOT / 'slopweb'))
    for name in ('SLOPD_DATA', 'XDG_DATA_HOME', 'SLOPD_ENDPOINT', 'SLOPWORLD_PROFILE'):
        monkeypatch.delenv(name, raising=False)
    return importlib.import_module('appearance')


def write_profile(profile: Path, text: str) -> None:
    settings = profile / 'Config/SlopWorld.toml'
    settings.parent.mkdir(parents=True, exist_ok=True)
    settings.write_text(text)


def test_host_and_browser_share_the_reviewed_presentation_boundary(appearance: Any, tmp_path: Path) -> None:
    # The two deployed products cannot read each other's implementation files.
    # Check exact parity in the checkout rather than introduce that dependency.
    managed = (ROOT / 'slopweb/runtime/Adapters/BrowserPreferences.cs').read_text()
    boundary = re.search(r'static readonly string\[\] Allowed\s*=\s*\[(.*?)\];', managed, re.DOTALL)
    assert boundary
    names = re.findall(r'"([a-zA-Z]+)"', boundary.group(1))
    assert len(names) == len(set(names))
    assert set(names) == appearance.PRESENTATION_PREFERENCES
    # All supported fields cross the host import; unrelated native settings and
    # credentials never enter browser assets, including a forged hostHome.
    text = '\n'.join(f'{name} = "selected-{name}"' for name in names)
    text += '\ntoken = "secret"\nendpoint = "private"\nautoConnect = false\n'
    text += 'commandPaletteHistory = "secret-command"\necoMode = true\nhostHome = "forged"\n'
    write_profile(tmp_path, text)
    imported = appearance.preferences(tmp_path)
    assert imported == {**{name: f'selected-{name}' for name in names}, 'hostHome': str(Path.home())}


def test_presentation_import_preserves_extended_preference_types(appearance: Any, tmp_path: Path) -> None:
    values = {
        'cursorColor': '#abcdef',
        'smoothScrolling': False,
        'statusbarUsage': False,
        'statusbarSummaryPosition': 'center',
        'statusbarClockPosition': 'hidden',
        'statusbarPlanner': False,
        'statusbarJukebox': False,
        'statusbarGM': False,
        'statusbarAgentIndicators': False,
        'sidebarShowHidden': True,
        'sidebarShowGitignored': True,
        'timeZone': 'UTC',
        'timeFormat': '12-hour',
        'dateFormat': 'dd/MM/yyyy',
        'ecoDim': 0.65,
    }
    write_profile(tmp_path, '\n'.join(f'{name} = {json.dumps(value)}' for name, value in values.items()))
    assert appearance.preferences(tmp_path) == {**values, 'hostHome': str(Path.home())}


@pytest.mark.parametrize('selection', ['xdg', 'data', 'endpoint', 'profile', 'explicit'])
def test_import_uses_shared_profile_selection(
    appearance: Any, tmp_path: Path, monkeypatch: pytest.MonkeyPatch, selection: str
) -> None:
    monkeypatch.setenv('XDG_DATA_HOME', str(tmp_path / 'xdg'))
    selected = tmp_path / 'xdg/slopworld/profiles/local'
    explicit = None
    if selection in ('data', 'endpoint', 'profile', 'explicit'):
        monkeypatch.setenv('SLOPD_DATA', str(tmp_path / 'data'))
        selected = tmp_path / 'data/profiles/local'
    if selection in ('endpoint', 'profile', 'explicit'):
        monkeypatch.setenv('SLOPD_ENDPOINT', str(tmp_path / 'endpoints/remote.toml'))
        selected = tmp_path / 'data/profiles/remote'
    if selection in ('profile', 'explicit'):
        monkeypatch.setenv('SLOPWORLD_PROFILE', str(tmp_path / 'chosen'))
        selected = tmp_path / 'chosen'
    if selection == 'explicit':
        explicit = selected = tmp_path / 'explicit'
    write_profile(tmp_path / 'xdg/slopworld/profiles/local', 'uiScheme = "wrong"')
    write_profile(selected, 'uiScheme = "selected"\necoDim = 0.65')
    assert appearance.preferences(explicit) == {
        'uiScheme': 'selected',
        'ecoDim': 0.65,
        'hostHome': str(Path.home()),
    }


def test_missing_selected_profile_keeps_defaults(appearance: Any, tmp_path: Path) -> None:
    assert appearance.preferences(tmp_path / 'missing') == {'hostHome': str(Path.home())}
