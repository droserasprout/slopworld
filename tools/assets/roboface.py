#!/usr/bin/env python3
"""Draw faceplate textures for agent pawns.

Usage: uv run --locked --extra assets python -m tools.assets.roboface [variant ...]
  Variants: blue red green purple yellow white missing (default: all)

Each variant creates RobotFace_{Variant}_{south,east}.png in the mod textures directory.
The tool requires NumPy and Pillow.

The tool renders the base plate and eyes separately at supersampled resolution.
It applies a box filter to make each part 128 by 128 pixels.
It then combines the parts.
This process keeps the base pixels identical across variants and changes only the eyes.

The faceplate is a geometric overlay, not a head.
Each constant below uses the 128 by 128 pixel head coordinate system.
The skull is approximately 47 pixels wide and has its center at (64, 64.5).
The tool clips the metal to the skull.

The tool does not create a north texture because the faceplate has no back.
The node leaves the pawn's head visible when the pawn faces away.
Graphic_Multi creates the west texture by mirroring the east texture.
"""

import argparse
import os
import sys
from typing import Protocol

import numpy as np
from PIL import Image

from tools import ROOT
from tools.assets import robot_geometry as geometry

OUT = ROOT / 'mod/Textures/SlopWorld'

# Metal fills the skull under the hair, so no hairstyle can expose a crown-side edge.
PLATE_TOP = geometry.CY - geometry.RY

# In profile the plate wraps the front of the head.
BACK_X = 56.0

# Base plate colors.
C_SOCKET = np.array([0.085, 0.095, 0.115])

# Eye glow colors.
EYE_COLORS = {
    'Blue': np.array([0.45, 0.75, 0.95]),
    'Red': np.array([0.95, 0.25, 0.15]),
    'Green': np.array([0.25, 0.85, 0.40]),
    'Purple': np.array([0.75, 0.35, 0.95]),
    'Yellow': np.array([0.95, 0.80, 0.20]),
    'White': np.array([0.85, 0.88, 0.95]),
}

# Missing variant colors.
C_VOID = np.array([0.02, 0.015, 0.025])
C_VOID_RIM = np.array([0.12, 0.06, 0.08])


class Layer(Protocol):
    def extract(self) -> tuple[geometry.FloatArray, geometry.FloatArray]: ...


class Renderer:
    """Supersampled RGBA buffer with box-filtered extraction."""

    def __init__(self) -> None:
        self.rgb: geometry.ColorBuffer = np.zeros((geometry.S, geometry.S, 3), np.float32)
        self.a: geometry.ColorBuffer = np.zeros((geometry.S, geometry.S), np.float32)

    def paint(self, mask: geometry.FloatArray, color: geometry.FloatArray) -> None:
        m = mask[..., None]
        self.rgb = self.rgb * (1 - m) + np.asarray(color, np.float32) * m
        self.a = np.maximum(self.a, mask)

    def extract(self) -> tuple[geometry.FloatArray, geometry.FloatArray]:
        """Box-filter down to N×N, returning straight-alpha RGBA."""
        px = (
            np.dstack([self.rgb, self.a]).reshape(geometry.N, geometry.SS, geometry.N, geometry.SS, 4).mean(axis=(1, 3))
        )
        rgb, a = px[..., :3], px[..., 3]
        rgb = np.where(a[..., None] > 1e-4, rgb / np.maximum(a[..., None], 1e-4), 0.0)
        return rgb, a


def front(r: Renderer, top_cut: geometry.FloatArray, *seamed_cuts: geometry.FloatArray) -> geometry.FloatArray:
    region = np.maximum(geometry.skull(geometry.SKIN_INSET), top_cut)
    for c in seamed_cuts:
        region = np.maximum(region, c)
    r.paint(geometry.cover(region), geometry.C_SEAM)
    inner = np.maximum(geometry.skull(geometry.SKIN_INSET + geometry.SEAM_W), top_cut)
    for c in seamed_cuts:
        inner = np.maximum(inner, c + geometry.SEAM_W)
    m = geometry.cover(inner)[..., None]
    r.rgb = (
        r.rgb * (1 - m)
        + geometry.steel(geometry.CX - geometry.RX, PLATE_TOP, geometry.CX + geometry.RX, geometry.CY + geometry.RY) * m
    )
    return region


def below(y: float) -> geometry.FloatArray:
    return y - geometry.Y


def ahead_of(x: float) -> geometry.FloatArray:
    return x - geometry.X


def clipped(mask: geometry.FloatArray, region: geometry.FloatArray) -> geometry.FloatArray:
    return mask * geometry.cover(region + geometry.SEAM_W)


def socket(r: Renderer, cx: float, cy: float) -> None:
    r.paint(geometry.cover(geometry.disc(cx, cy, geometry.EYE_R)), C_SOCKET)


def iris(r: Renderer, cx: float, cy: float, glow: geometry.FloatArray) -> None:
    """Glowing lens inside the socket. Painted on its own layer so the base
    plate is rendered without eyes and composited later."""
    m = geometry.cover(geometry.disc(cx, cy, geometry.IRIS_R))
    g = np.clip(
        1.25 - ((geometry.X - (cx - geometry.IRIS_R)) + (geometry.Y - (cy - geometry.IRIS_R))) / (geometry.IRIS_R * 4),
        0.35,
        1.0,
    )
    r.rgb = r.rgb * (1 - m[..., None]) + (glow * (g * 0.95)[..., None]) * m[..., None]
    r.a = np.maximum(r.a, m)


def void_eye(r: Renderer, cx: float, cy: float) -> None:
    """Void hole: black, with a faint red rim. Same layer as iris (overpaints
    the socket in the composite)."""
    inner = geometry.cover(geometry.disc(cx, cy, geometry.EYE_R - 1.4))
    r.paint(inner, C_VOID)
    rim = geometry.cover(geometry.disc(cx, cy, geometry.EYE_R)) - inner
    r.paint(np.clip(rim, 0, 1), C_VOID_RIM)


def mouth(r: Renderer, x0: float, y0: float, x1: float, y1: float, bars: tuple[float, ...]) -> None:
    r.paint(geometry.cover(geometry.rrect(x0, y0, x1, y1, geometry.MOUTH_R)), C_SOCKET)
    inside = geometry.cover(geometry.rrect(x0 + 0.9, y0 + 0.9, x1 - 0.9, y1 - 0.9, geometry.MOUTH_R * 0.6))
    for x in bars:
        r.paint(
            geometry.cover(geometry.rrect(x - geometry.BAR_W, y0, x + geometry.BAR_W, y1, geometry.BAR_W * 0.8))
            * inside,
            geometry.C_PLATE_BOT,
        )


def bolt(r: Renderer, cx: float, cy: float, region: geometry.FloatArray) -> None:
    r.paint(clipped(geometry.cover(geometry.disc(cx, cy, geometry.BOLT_R)), region), geometry.C_BOLT)
    r.paint(clipped(geometry.cover(geometry.disc(cx, cy, geometry.BOLT_R * 0.45)), region), geometry.C_SEAM)


def build_base(facing: str) -> Renderer:
    """Render the base plate (no eyes) at supersampled resolution."""
    r = Renderer()
    if facing == 'south':
        region = front(r, below(PLATE_TOP))
        for x in geometry.EYES_X:
            socket(r, x, geometry.EYE_Y)
        mouth(r, *geometry.MOUTH, geometry.BARS_X)
        for cx, cy in geometry.BOLTS:
            bolt(r, cx, cy, region)
    else:
        region = front(r, below(PLATE_TOP), ahead_of(BACK_X))
        socket(r, 73.5, geometry.EYE_Y)
        mouth(r, 63.0, geometry.MOUTH[1], 80.0, geometry.MOUTH[3], (66.0, 69.5, 73.0, 76.5))
        for cy in (59.5, 84.0):
            bolt(r, BACK_X + 4.0, cy, region)
    return r


def build_eyes(facing: str, glow_or_none: geometry.FloatArray | None) -> Renderer:
    """Render the eyes at supersampled resolution. glow_or_none is an RGB array
    for a colored glow, or None for void holes."""
    r = Renderer()
    if facing == 'south':
        positions = [(x, geometry.EYE_Y) for x in geometry.EYES_X]
    else:
        positions = [(73.5, geometry.EYE_Y)]
    if glow_or_none is not None:
        for cx, cy in positions:
            iris(r, cx, cy, glow_or_none)
    else:
        for cx, cy in positions:
            void_eye(r, cx, cy)
    return r


def composite(base_r: Layer, eye_r: Layer) -> tuple[geometry.FloatArray, geometry.FloatArray]:
    """Composite eye layer over base at final (N×N) resolution."""
    base_rgb, base_a = base_r.extract()
    eye_rgb, eye_a = eye_r.extract()
    a = eye_a + base_a * (1 - eye_a)
    premultiplied = base_rgb * (base_a * (1 - eye_a))[..., None] + eye_rgb * eye_a[..., None]
    rgb = np.divide(premultiplied, a[..., None], out=np.zeros_like(premultiplied), where=a[..., None] > 0)
    return rgb, a


def save(rgb: geometry.FloatArray, a: geometry.FloatArray, name: str) -> None:
    img = np.clip(np.concatenate([rgb, a[..., None]], -1), 0, 1)
    path = os.path.join(OUT, f'RobotFace_{name}.png')
    Image.fromarray((img * 255).astype(np.uint8)).save(path)
    print('wrote', os.path.normpath(path))


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('variants', nargs='*', type=str.lower, choices=[*map(str.lower, EYE_COLORS), 'missing', 'all'])
    args = parser.parse_args(argv)
    registry = {**EYE_COLORS, 'Missing': None}
    wanted = list(registry) if not args.variants or 'all' in args.variants else [v.title() for v in args.variants]
    bases = {f: build_base(f) for f in ('south', 'east')}
    OUT.mkdir(parents=True, exist_ok=True)
    for variant in wanted:
        for facing in ('south', 'east'):
            eye_r = build_eyes(facing, registry[variant])
            rgb, a = composite(bases[facing], eye_r)
            save(rgb, a, f'{variant}_{facing}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
