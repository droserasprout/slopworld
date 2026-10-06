"""Alpha regression checks use synthetic arrays without rendering assets."""

from pathlib import Path
from types import SimpleNamespace

import pytest
from PIL import Image

np = pytest.importorskip('numpy')
pytest.importorskip('PIL')


def test_icon_overlap_uses_source_over_and_returns_straight_alpha(monkeypatch: pytest.MonkeyPatch) -> None:
    from tools.assets import appicon

    rose = np.array([[[0, 255, 0, 127.5]]], dtype=np.float32)
    robot = np.zeros((appicon.N, appicon.N, 4), dtype=np.float32)
    robot[..., 0] = 1
    robot[..., 3] = 0.5
    monkeypatch.setattr(Image, 'open', lambda _path: rose)
    monkeypatch.setattr(appicon, 'draw_robot', lambda: robot)
    result = appicon.make_icon('unused')
    np.testing.assert_allclose(result[56, 64], [2 / 3, 1 / 3, 0, 0.75])
    np.testing.assert_allclose(result[0, 0], [1, 0, 0, 0.5])


def test_translucent_eye_edges_do_not_add_opaque_color() -> None:
    from tools.assets import roboface

    base = SimpleNamespace(extract=lambda: (np.array([[[0.0, 0.0, 1.0]]]), np.array([[0.5]])))
    eyes = SimpleNamespace(extract=lambda: (np.array([[[1.0, 0.0, 0.0]]]), np.array([[0.5]])))
    rgb, alpha = roboface.composite(base, eyes)
    np.testing.assert_allclose(rgb, [[[2 / 3, 0, 1 / 3]]])
    np.testing.assert_allclose(alpha, [[0.75]])


@pytest.mark.parametrize(
    'arguments,variants',
    [
        ([], ['Blue', 'Red', 'Green', 'Purple', 'Yellow', 'White', 'Missing']),
        (['all'], ['Blue', 'Red', 'Green', 'Purple', 'Yellow', 'White', 'Missing']),
        (['blue'], ['Blue']),
        (['BLUE'], ['Blue']),
    ],
)
def test_robot_variant_cli_matches_documented_names(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path, arguments: list[str], variants: list[str]
) -> None:
    from tools.assets import roboface

    written = []
    monkeypatch.setattr(roboface, 'OUT', tmp_path)
    monkeypatch.setattr(roboface, 'build_base', lambda _facing: None)
    monkeypatch.setattr(roboface, 'build_eyes', lambda _facing, _glow: None)
    monkeypatch.setattr(roboface, 'composite', lambda _base, _eyes: (None, None))
    monkeypatch.setattr(roboface, 'save', lambda _rgb, _alpha, name: written.append(name))
    assert roboface.main(arguments) == 0
    assert written == [f'{variant}_{facing}' for variant in variants for facing in ('south', 'east')]
