"""Shared robot geometry, detail painting, and steel shading in head coordinates.

Generators own their cuts, eye palettes, and alpha compositing policies.
"""

from typing import Protocol
from typing import cast

import numpy as np
from numpy.typing import NDArray

type FloatArray = NDArray[np.float64]
type ColorBuffer = NDArray[np.float32] | FloatArray
N = 128
SS = 4
S = N * SS
# Superellipse centred at (64, 64.5), spanning x 40.5..87.5 and y 40..89.
CX, CY, RX, RY, EXP = 64.0, 64.5, 23.5, 24.5, 3.0
SKIN_INSET = 2.0
SEAM_W = 1.6
EYE_Y, EYE_R, IRIS_R = 64.0, 5.8, 3.5
EYES_X = (57.0, 71.0)
MOUTH = (53.5, 74.0, 74.5, 80.5)
MOUTH_R = 2.8
BARS_X = (56.5, 60.5, 64.5, 68.5, 72.5)
BAR_W = 0.8
BOLTS = ((52.0, 58.0), (76.0, 58.0), (52.5, 84.0), (75.5, 84.0))
BOLT_R = 1.2
C_PLATE_TOP = np.array([0.42, 0.435, 0.46])
C_PLATE_BOT = np.array([0.25, 0.265, 0.29])
C_SEAM = np.array([0.16, 0.175, 0.195])
C_BOLT = np.array([0.52, 0.535, 0.56])
_y, _x = np.mgrid[0:S, 0:S]
X: FloatArray = (_x + 0.5) / SS
Y: FloatArray = (_y + 0.5) / SS


class Painter(Protocol):
    """Generators composite each shared detail with their own alpha policy."""

    def paint(self, mask: FloatArray, color: FloatArray) -> None: ...


def below(y: float) -> FloatArray:
    return y - Y


def clipped(mask: FloatArray, region: FloatArray) -> FloatArray:
    return mask * cover(region + SEAM_W)


def paint_mouth(
    painter: Painter, x0: float, y0: float, x1: float, y1: float, bars: tuple[float, ...], socket_color: FloatArray
) -> None:
    painter.paint(cover(rrect(x0, y0, x1, y1, MOUTH_R)), socket_color)
    inside = cover(rrect(x0 + 0.9, y0 + 0.9, x1 - 0.9, y1 - 0.9, MOUTH_R * 0.6))
    for x in bars:
        painter.paint(cover(rrect(x - BAR_W, y0, x + BAR_W, y1, BAR_W * 0.8)) * inside, C_PLATE_BOT)


def paint_bolt(painter: Painter, cx: float, cy: float, region: FloatArray) -> None:
    painter.paint(clipped(cover(disc(cx, cy, BOLT_R)), region), C_BOLT)
    painter.paint(clipped(cover(disc(cx, cy, BOLT_R * 0.45)), region), C_SEAM)


def cover(sd: FloatArray) -> FloatArray:
    return np.clip(0.5 - sd * SS, 0.0, 1.0)


def rrect(x0: float, y0: float, x1: float, y1: float, r: float) -> FloatArray:
    hx, hy = (x1 - x0) / 2 - r, (y1 - y0) / 2 - r
    dx = np.abs(X - (x0 + x1) / 2) - hx
    dy = np.abs(Y - (y0 + y1) / 2) - hy
    outside = np.sqrt(np.maximum(dx, 0) ** 2 + np.maximum(dy, 0) ** 2)
    return cast(FloatArray, outside + np.minimum(np.maximum(dx, dy), 0) - r)


def disc(cx: float, cy: float, r: float) -> FloatArray:
    return np.sqrt((X - cx) ** 2 + (Y - cy) ** 2) - r


def skull(inset: float = 0.0) -> FloatArray:
    dx, dy = (X - CX) / RX, (Y - CY) / RY
    f = np.abs(dx) ** EXP + np.abs(dy) ** EXP
    return (f ** (1.0 / EXP) - 1.0) * min(RX, RY) + inset


def steel(x0: float, y0: float, x1: float, y1: float) -> FloatArray:
    t = np.clip((Y - y0) / (y1 - y0), 0, 1)[..., None]
    lit = np.clip(1.0 - (((X - (x0 + (x1 - x0) * 0.3)) ** 2 + (Y - (y0 + (y1 - y0) * 0.25)) ** 2) / 900.0), 0, 1)
    return np.clip(C_PLATE_TOP * (1 - t) + C_PLATE_BOT * t + lit[..., None] * 0.09, 0, 1)
