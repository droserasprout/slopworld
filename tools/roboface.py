#!/usr/bin/env python3
"""Draws faceplate textures for agent pawns.

Usage: python3 tools/roboface.py [variant ...]
  Variants: blue red green purple yellow white missing (default: all)

Each variant generates RobotFace_{Variant}_{south,east}.png in the mod textures
directory. Needs numpy and pillow.

The base plate (metal, mouth, bolts, sockets) and the eyes are rendered
separately at supersampled resolution, then each is box-filtered down to the
final 128×128 before compositing. This guarantees the base is pixel-identical
across all variants — only the eye region changes.

The faceplate is geometry, not art. It is an overlay, not a head. Every constant
below is in the *head's* frame - a 128x128 texture whose skull is a ~47px blob
centred at (64, 64.5) - and the metal is clipped to that skull rather than drawn
as a shape of its own.

No north texture: a faceplate has no back, and the node's visibleFacing leaves
the pawn's own head showing when it turns away. West is Graphic_Multi's mirror.
"""
import os
import sys

import numpy as np
from PIL import Image

OUT = os.path.join(os.path.dirname(__file__), "..", "mod", "Textures", "SlopWorld")

N = 128                     # the head frame RimWorld draws us into
SS = 4                      # supersample factor
S = N * SS

# The shape the metal is cut against: a superellipse centred at (CX, CY),
# spanning x 40.5..87.5 and y 40..89.
CX, CY, RX, RY, EXP = 64.0, 64.5, 23.5, 24.5, 3.0

HAIRLINE = 54.0
SKIN_INSET = 2.0
SEAM_W = 1.6

# In profile the plate wraps the front of the head.
BACK_X = 56.0

EYE_Y, EYE_R, IRIS_R = 64.0, 5.8, 3.5
EYES_X = (57.0, 71.0)

MOUTH = (53.5, 74.0, 74.5, 80.5)
MOUTH_R = 2.8
BARS_X = (56.5, 60.5, 64.5, 68.5, 72.5)
BAR_W = 0.8

BOLTS = ((52.0, 58.0), (76.0, 58.0), (52.5, 84.0), (75.5, 84.0))
BOLT_R = 1.2

# Base plate colours.
C_PLATE_TOP = np.array([0.42, 0.435, 0.46])
C_PLATE_BOT = np.array([0.25, 0.265, 0.29])
C_SEAM = np.array([0.16, 0.175, 0.195])
C_SOCKET = np.array([0.085, 0.095, 0.115])
C_BOLT = np.array([0.52, 0.535, 0.56])

# Eye glow colours.
EYE_COLORS = {
    "Blue": np.array([0.45, 0.75, 0.95]),
    "Red": np.array([0.95, 0.25, 0.15]),
    "Green": np.array([0.25, 0.85, 0.40]),
    "Purple": np.array([0.75, 0.35, 0.95]),
    "Yellow": np.array([0.95, 0.80, 0.20]),
    "White": np.array([0.85, 0.88, 0.95]),
}

# Missing variant colours.
C_VOID = np.array([0.02, 0.015, 0.025])
C_VOID_RIM = np.array([0.12, 0.06, 0.08])

_y, _x = np.mgrid[0:S, 0:S]
X, Y = (_x + 0.5) / SS, (_y + 0.5) / SS


def cover(sd):
    return np.clip(0.5 - sd * SS, 0.0, 1.0)


def rrect(x0, y0, x1, y1, r):
    hx, hy = (x1 - x0) / 2 - r, (y1 - y0) / 2 - r
    dx = np.abs(X - (x0 + x1) / 2) - hx
    dy = np.abs(Y - (y0 + y1) / 2) - hy
    outside = np.sqrt(np.maximum(dx, 0) ** 2 + np.maximum(dy, 0) ** 2)
    return outside + np.minimum(np.maximum(dx, dy), 0) - r


def disc(cx, cy, r):
    return np.sqrt((X - cx) ** 2 + (Y - cy) ** 2) - r


def skull(inset=0.0):
    dx, dy = (X - CX) / RX, (Y - CY) / RY
    f = np.abs(dx) ** EXP + np.abs(dy) ** EXP
    return (f ** (1.0 / EXP) - 1.0) * min(RX, RY) + inset


class Renderer:
    """Supersampled RGBA buffer with box-filtered extraction."""

    def __init__(self):
        self.rgb = np.zeros((S, S, 3), np.float32)
        self.a = np.zeros((S, S), np.float32)

    def paint(self, mask, color):
        m = mask[..., None]
        self.rgb = self.rgb * (1 - m) + np.asarray(color, np.float32) * m
        self.a = np.maximum(self.a, mask)

    def extract(self):
        """Box-filter down to N×N, returning straight-alpha RGBA."""
        px = np.dstack([self.rgb, self.a]).reshape(N, SS, N, SS, 4).mean(axis=(1, 3))
        rgb, a = px[..., :3], px[..., 3]
        rgb = np.where(a[..., None] > 1e-4, rgb / np.maximum(a[..., None], 1e-4), 0.0)
        return rgb, a


def steel(x0, y0, x1, y1):
    t = np.clip((Y - y0) / (y1 - y0), 0, 1)[..., None]
    lit = np.clip(1.0 - (((X - (x0 + (x1 - x0) * 0.3)) ** 2
                          + (Y - (y0 + (y1 - y0) * 0.25)) ** 2) / 900.0), 0, 1)
    return np.clip(C_PLATE_TOP * (1 - t) + C_PLATE_BOT * t + lit[..., None] * 0.09, 0, 1)


def front(r, *cuts):
    region = skull(SKIN_INSET)
    for c in cuts:
        region = np.maximum(region, c)
    r.paint(cover(region), C_SEAM)
    inner = skull(SKIN_INSET)
    for c in cuts:
        inner = np.maximum(inner, c + SEAM_W)
    m = cover(inner)[..., None]
    r.rgb = r.rgb * (1 - m) + steel(CX - RX, HAIRLINE, CX + RX, CY + RY) * m
    return region


def below(y):
    return y - Y


def ahead_of(x):
    return x - X


def clipped(mask, region):
    return mask * cover(region + SEAM_W)


def socket(r, cx, cy):
    r.paint(cover(disc(cx, cy, EYE_R)), C_SOCKET)


def iris(r, cx, cy, glow):
    """Glowing lens inside the socket. Painted on its own layer so the base
    plate is rendered without eyes and composited later."""
    m = cover(disc(cx, cy, IRIS_R))
    g = np.clip(1.25 - ((X - (cx - IRIS_R)) + (Y - (cy - IRIS_R))) / (IRIS_R * 4),
                0.35, 1.0)
    r.rgb = r.rgb * (1 - m[..., None]) + (glow * (g * 0.95)[..., None]) * m[..., None]
    r.a = np.maximum(r.a, m)


def void_eye(r, cx, cy):
    """Void hole: black, with a faint red rim. Same layer as iris (overpaints
    the socket in the composite)."""
    inner = cover(disc(cx, cy, EYE_R - 1.4))
    r.paint(inner, C_VOID)
    rim = cover(disc(cx, cy, EYE_R)) - inner
    r.paint(np.clip(rim, 0, 1), C_VOID_RIM)


def mouth(r, x0, y0, x1, y1, bars):
    r.paint(cover(rrect(x0, y0, x1, y1, MOUTH_R)), C_SOCKET)
    inside = cover(rrect(x0 + 0.9, y0 + 0.9, x1 - 0.9, y1 - 0.9, MOUTH_R * 0.6))
    for x in bars:
        r.paint(cover(rrect(x - BAR_W, y0, x + BAR_W, y1, BAR_W * 0.8)) * inside, C_PLATE_BOT)


def bolt(r, cx, cy, region):
    r.paint(clipped(cover(disc(cx, cy, BOLT_R)), region), C_BOLT)
    r.paint(clipped(cover(disc(cx, cy, BOLT_R * 0.45)), region), C_SEAM)


def build_base(facing):
    """Render the base plate (no eyes) at supersampled resolution."""
    r = Renderer()
    if facing == "south":
        region = front(r, below(HAIRLINE))
        for x in EYES_X:
            socket(r, x, EYE_Y)
        mouth(r, *MOUTH, BARS_X)
        for cx, cy in BOLTS:
            bolt(r, cx, cy, region)
    else:
        region = front(r, below(HAIRLINE), ahead_of(BACK_X))
        socket(r, 73.5, EYE_Y)
        mouth(r, 63.0, MOUTH[1], 80.0, MOUTH[3], (66.0, 69.5, 73.0, 76.5))
        for cy in (59.5, 84.0):
            bolt(r, BACK_X + 4.0, cy, region)
    return r


def build_eyes(facing, glow_or_none):
    """Render the eyes at supersampled resolution. glow_or_none is an RGB array
    for a coloured glow, or None for void holes."""
    r = Renderer()
    if facing == "south":
        positions = [(x, EYE_Y) for x in EYES_X]
    else:
        positions = [(73.5, EYE_Y)]
    if glow_or_none is not None:
        for cx, cy in positions:
            iris(r, cx, cy, glow_or_none)
    else:
        for cx, cy in positions:
            void_eye(r, cx, cy)
    return r


def composite(base_r, eye_r):
    """Composite eye layer over base at final (N×N) resolution."""
    base_rgb, base_a = base_r.extract()
    eye_rgb, eye_a = eye_r.extract()
    # Straight alpha composite: result = base * (1 - eye_a) + eye
    rgb = base_rgb * (1 - eye_a[..., None]) + eye_rgb
    a = np.maximum(base_a, eye_a)
    return rgb, a


def save(rgb, a, name):
    img = np.clip(np.concatenate([rgb, a[..., None]], -1), 0, 1)
    path = os.path.join(OUT, f"RobotFace_{name}.png")
    Image.fromarray((img * 255).astype(np.uint8)).save(path)
    print("wrote", os.path.normpath(path))


if __name__ == "__main__":
    # Build the base plates once per facing — these are the same for every variant.
    bases = {f: build_base(f) for f in ("south", "east")}

    wanted = sys.argv[1:] if len(sys.argv) > 1 else list(EYE_COLORS.keys()) + ["Missing"]

    for variant in wanted:
        if variant == "Missing":
            glow = None
        elif variant in EYE_COLORS:
            glow = EYE_COLORS[variant]
        else:
            print(f"unknown variant '{variant}'; choose from {list(EYE_COLORS.keys())} + Missing")
            continue

        for facing in ("south", "east"):
            eye_r = build_eyes(facing, glow)
            rgb, a = composite(bases[facing], eye_r)
            save(rgb, a, f"{variant}_{facing}")