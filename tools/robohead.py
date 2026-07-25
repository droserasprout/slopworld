#!/usr/bin/env python3
"""Draws mod/Textures/SlopWorld/RobotHead_{south,east,north}.png.

The agent head is geometry, not art, so it lives here as code: run
`python3 tools/robohead.py` after changing a constant and the textures are
rebuilt. Needs numpy and pillow.
"""
import os

import numpy as np
from PIL import Image

OUT = os.path.join(os.path.dirname(__file__), "..", "mod", "Textures", "SlopWorld")

N = 128                     # RimWorld heads are 128x128
SS = 4                      # supersample factor, box-filtered down at the end
S = N * SS

# A vanilla head is a ~46px blob centred in the frame. Stray from that and the
# head sits off the neck, because the body's headOffset assumes it.
CX, CY, RX, RY, EXP = 64.0, 64.5, 23.5, 24.5, 3.0
OUTLINE_W = 3.0

C_OUT = np.array([0.09, 0.09, 0.10])
C_TOP = np.array([0.62, 0.645, 0.67])
C_BOT = np.array([0.34, 0.355, 0.375])
C_PLATE = np.array([0.27, 0.285, 0.31])
C_SEAM = np.array([0.21, 0.225, 0.245])
C_LENS = np.array([0.085, 0.095, 0.115])
C_GLOW = np.array([0.45, 0.75, 0.95])   # the "working" blue, straight from the UI

_y, _x = np.mgrid[0:S, 0:S]
X, Y = (_x + 0.5) / SS, (_y + 0.5) / SS


def cover(sd):
    """Signed distance in pixels (negative inside) -> coverage of a sample."""
    return np.clip(0.5 - sd * SS, 0.0, 1.0)


def superellipse(cx, cy, rx, ry, n=EXP):
    dx, dy = (X - cx) / rx, (Y - cy) / ry
    f = np.abs(dx) ** n + np.abs(dy) ** n
    # Crude but stable distance: scale the implicit value back into pixels.
    return (f ** (1.0 / n) - 1.0) * min(rx, ry)


def rrect(x0, y0, x1, y1, r):
    hx, hy = (x1 - x0) / 2 - r, (y1 - y0) / 2 - r
    dx = np.abs(X - (x0 + x1) / 2) - hx
    dy = np.abs(Y - (y0 + y1) / 2) - hy
    outside = np.sqrt(np.maximum(dx, 0) ** 2 + np.maximum(dy, 0) ** 2)
    return outside + np.minimum(np.maximum(dx, dy), 0) - r


def disc(cx, cy, r):
    return np.sqrt((X - cx) ** 2 + (Y - cy) ** 2) - r


class Head:
    def __init__(self):
        self.rgb = np.zeros((S, S, 3), np.float32)
        self.a = np.zeros((S, S), np.float32)

    def paint(self, mask, color):
        m = mask[..., None]
        self.rgb = self.rgb * (1 - m) + np.asarray(color, np.float32) * m
        self.a = np.maximum(self.a, mask)

    def save(self, name):
        px = np.dstack([self.rgb, self.a]).reshape(N, SS, N, SS, 4).mean(axis=(1, 3))
        rgb, a = px[..., :3], px[..., 3:]
        # The box filter mixes the transparent margin in, leaving the colours
        # premultiplied; RimWorld wants straight alpha.
        rgb = np.where(a > 1e-4, rgb / np.maximum(a, 1e-4), 0.0)
        img = np.clip(np.concatenate([rgb, a], -1), 0, 1)
        path = os.path.join(OUT, f"RobotHead_{name}.png")
        Image.fromarray((img * 255).astype(np.uint8)).save(path)
        print("wrote", os.path.normpath(path))


def metal(skull):
    """Steel: a vertical gradient, a lit patch up left, darkening to the rim."""
    t = np.clip((Y - (CY - RY)) / (2 * RY), 0, 1)[..., None]
    lit = np.clip(1.0 - ((X - (CX - 9)) ** 2 + (Y - (CY - 14)) ** 2) / 1500.0, 0, 1)
    base = C_TOP * (1 - t) + C_BOT * t + lit[..., None] * 0.10
    rim = np.clip(-skull / 6.0, 0, 1)[..., None]     # 0 at the edge, 1 well inside
    return np.clip(base * (0.72 + 0.28 * rim), 0, 1)


def shell(h):
    """Outline and body, shared by every rotation."""
    skull = superellipse(CX, CY, RX, RY)
    # The outline is part of the silhouette, as in vanilla: the metal is inset.
    h.paint(cover(skull), C_OUT)
    body = cover(skull + OUTLINE_W)[..., None]
    h.rgb = h.rgb * (1 - body) + metal(skull) * body
    return skull


def clipped(mask, skull, inset=OUTLINE_W + 0.5):
    """Keeps a detail off the outline, whatever the rotation does to it."""
    return mask * cover(skull + inset)


def lens(h, skull, x0, x1, y0, y1, r):
    h.paint(clipped(cover(rrect(x0, y0, x1, y1, r)), skull), C_LENS)
    glow = clipped(cover(rrect(x0 + 2.5, y0 + 2.5, x1 - 2.5, y1 - 2.5, r * 0.6)), skull)
    # Brightest at its left end, so the face reads as lit from that side.
    g = np.clip(1.15 - (X - (x0 + 3)) / (x1 - x0) * 1.15, 0.25, 1.0)
    h.rgb = h.rgb * (1 - glow[..., None]) + C_GLOW * (g * 0.9)[..., None] * glow[..., None]
    h.a = np.maximum(h.a, glow)


def south():
    """The face: crown seam, lens, vented jaw plate, bolts for ears."""
    h = Head()
    skull = shell(h)
    h.paint(clipped(cover(rrect(46, 47.5, 82, 49.5, 1.0)), skull), C_SEAM)
    lens(h, skull, 45, 83, 57.5, 70.5, 5.0)
    h.paint(clipped(cover(rrect(52, 76, 76, 84, 3.0)), skull), C_PLATE)
    for x in (57, 64, 71):
        h.paint(clipped(cover(rrect(x - 0.9, 77.5, x + 0.9, 82.5, 0.8)), skull), C_SEAM)
    for x in (43.5, 84.5):
        h.paint(clipped(cover(disc(x, 64, 3.4)), skull), C_PLATE)
        h.paint(clipped(cover(disc(x, 64, 1.4)), skull), C_SEAM)
    return h


def east():
    """Facing right: the lens wraps the front, the side is a plated ear disc."""
    h = Head()
    skull = shell(h)
    h.paint(clipped(cover(rrect(48, 47.5, 78, 49.5, 1.0)), skull), C_SEAM)
    lens(h, skull, 66, 88, 57.5, 70.5, 4.5)
    h.paint(clipped(cover(disc(55, 66, 8.0)), skull), C_PLATE)
    h.paint(clipped(cover(disc(55, 66, 3.2)), skull), C_SEAM)
    h.paint(clipped(cover(rrect(60, 76, 80, 83, 3.0)), skull), C_PLATE)
    return h


def north():
    """The back: a seam down the middle, a cooling vent, two bolts."""
    h = Head()
    skull = shell(h)
    h.paint(clipped(cover(rrect(63.2, 44, 64.8, 84, 0.7)), skull), C_SEAM)
    for y in (54, 59, 64):
        h.paint(clipped(cover(rrect(52, y - 0.9, 76, y + 0.9, 0.8)), skull), C_SEAM)
    for x in (49, 79):
        h.paint(clipped(cover(disc(x, 74, 3.2)), skull), C_PLATE)
        h.paint(clipped(cover(disc(x, 74, 1.3)), skull), C_SEAM)
    return h


if __name__ == "__main__":
    for name, draw in (("south", south), ("east", east), ("north", north)):
        draw().save(name)
