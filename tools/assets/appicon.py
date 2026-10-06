#!/usr/bin/env python3
"""Draw the application icon with a robot faceplate and a wilted rose.

The tool gets the rose from Noto Color Emoji.
It puts the rose behind the faceplate and above the skull.

Process:
  1. Render the rose as SlopWorld_rose.png.
  2. Put the faceplate over the rose.
  3. Move the stem tip slightly into the top of the skull.

Usage: just refresh-appicon

Output: mod/Textures/SlopWorld/SlopWorld_icon.png (128x128 RGBA).

The tool requires NumPy, Pillow, pycairo, Pango, and Fontconfig; Noto Color Emoji is bundled.
"""

import os
import sys
from typing import cast

import numpy as np
from numpy.typing import NDArray
from PIL import Image

from tools import ROOT
from tools.assets import robot_geometry as geometry

# Icon-specific cut and eye palette; shared geometry lives in robot_geometry.
HAIRLINE = 54.0

C_LENS = np.array([0.085, 0.095, 0.115])
C_GLOW = np.array([0.45, 0.75, 0.95])


class Face:
    def __init__(self) -> None:
        self.rgb: geometry.ColorBuffer = np.zeros((geometry.S, geometry.S, 3), np.float32)
        self.a: geometry.ColorBuffer = np.zeros((geometry.S, geometry.S), np.float32)

    def paint(self, mask: geometry.FloatArray, color: geometry.FloatArray) -> None:
        m = mask[..., None]
        self.rgb = self.rgb * (1 - m) + np.asarray(color, np.float32) * m
        self.a = mask + self.a * (1 - mask)


def front(f: Face, *cuts: geometry.FloatArray) -> geometry.FloatArray:
    region = geometry.skull(geometry.SKIN_INSET)
    for c in cuts:
        region = np.maximum(region, c)
    f.paint(geometry.cover(region), geometry.C_SEAM)
    inner = geometry.skull(geometry.SKIN_INSET)
    for c in cuts:
        inner = np.maximum(inner, c + geometry.SEAM_W)
    m = geometry.cover(inner)[..., None]
    f.rgb = (
        f.rgb * (1 - m)
        + geometry.steel(geometry.CX - geometry.RX, HAIRLINE, geometry.CX + geometry.RX, geometry.CY + geometry.RY) * m
    )
    return region


def eye(f: Face, cx: float, cy: float) -> None:
    f.paint(geometry.cover(geometry.disc(cx, cy, geometry.EYE_R)), C_LENS)
    iris = geometry.cover(geometry.disc(cx, cy, geometry.IRIS_R))
    g = np.clip(
        1.25 - ((geometry.X - (cx - geometry.IRIS_R)) + (geometry.Y - (cy - geometry.IRIS_R))) / (geometry.IRIS_R * 4),
        0.35,
        1.0,
    )
    f.rgb = f.rgb * (1 - iris[..., None]) + C_GLOW * (g * 0.95)[..., None] * iris[..., None]
    f.a = iris + f.a * (1 - iris)


def draw_robot() -> geometry.FloatArray:
    f = Face()
    region = front(f, geometry.below(HAIRLINE))
    for x in geometry.EYES_X:
        eye(f, x, geometry.EYE_Y)
    geometry.paint_mouth(f, *geometry.MOUTH, geometry.BARS_X, C_LENS)
    for cx, cy in geometry.BOLTS:
        geometry.paint_bolt(f, cx, cy, region)
    px = np.dstack([f.rgb, f.a]).reshape(geometry.N, geometry.SS, geometry.N, geometry.SS, 4).mean(axis=(1, 3))
    rgb, a = px[..., :3], px[..., 3:]
    rgb = np.where(a > 1e-4, rgb / np.maximum(a, 1e-4), 0.0)
    return cast(geometry.FloatArray, np.clip(np.concatenate([rgb, a], -1), 0, 1))


# ── step 1: render rose as a separate PNG ─────────────────────────────────


def render_rose_png(target_h: int, out_path: str) -> tuple[NDArray[np.uint8], int, int]:
    """Render 🥀 via PangoCairo, save as a standalone RGBA PNG at target_h high."""
    from tools.assets.emoji import render

    arr = render('🥀', 256)
    ys, xs = np.nonzero(arr[..., 3] > 10 / 255.0)
    if not len(ys):
        raise RuntimeError('bundled Noto Color Emoji did not render the rose')
    rose = arr[ys.min() : ys.max() + 1, xs.min() : xs.max() + 1]
    # PNG and Pillow RGBA expect straight-alpha color.
    alpha = rose[..., 3:]
    rose[..., :3] = np.divide(rose[..., :3], alpha, out=np.zeros_like(rose[..., :3]), where=alpha > 0)

    # Scale to target_h
    rh, rw = rose.shape[:2]
    scale = target_h / rh
    new_w = max(1, int(round(rw * scale)))
    rose_pil = Image.fromarray((rose * 255).astype(np.uint8))
    rose_scaled = np.array(rose_pil.resize((new_w, target_h), Image.Resampling.LANCZOS), dtype=np.uint8)
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    Image.fromarray(rose_scaled).save(out_path)
    print('wrote', os.path.normpath(out_path))
    return rose_scaled, new_w, target_h


# ── step 2: composite ─────────────────────────────────────────────────────


def make_icon(rose_path: str) -> NDArray[np.float32]:
    # Load the rose PNG first: it is the layer *below* the robot face.
    rose_pil = Image.open(rose_path)
    rose = np.array(rose_pil, dtype=np.float32) / 255.0
    rh, rw = rose.shape[:2]

    # The rose grows from the top of the skull, so it sits higher than the
    # face. The stem tip (bottom row of the rose image) pokes into the top of
    # the faceplate at the hairline, making it read as growing *out of* the
    # head.
    stem_tip_y = 56  # hairline + 2, rose scaled down so needs lowering
    rose_top = stem_tip_y - rh + 1  # stem_tip_y = rose_top + rh - 1
    rose_left = int(round(geometry.CX - rw / 2))  # centred on the faceplate

    # Composite: rose behind, robot on top.
    icon = np.zeros((geometry.N, geometry.N, 4), dtype=np.float32)

    # Paste the rose first.
    y0 = max(0, rose_top)
    y1 = min(geometry.N, rose_top + rh)
    x0 = max(0, rose_left)
    x1 = min(geometry.N, rose_left + rw)
    ry0 = y0 - rose_top
    rx0 = x0 - rose_left
    if y1 > y0 and x1 > x0:
        r_alpha = rose[ry0 : ry0 + y1 - y0, rx0 : rx0 + x1 - x0, 3]
        r_rgb = rose[ry0 : ry0 + y1 - y0, rx0 : rx0 + x1 - x0, :3]
        icon[y0:y1, x0:x1, :3] = r_rgb * r_alpha[..., None]
        icon[y0:y1, x0:x1, 3] = np.maximum(icon[y0:y1, x0:x1, 3], r_alpha)

    # Robot face on top, covering the rose's stem where they overlap.
    robot = draw_robot()
    ra = robot[:, :, 3]
    icon[:, :, :3] = robot[:, :, :3] * ra[..., None] + icon[:, :, :3] * (1 - ra[..., None])
    icon[:, :, 3] = ra + icon[:, :, 3] * (1 - ra)

    alpha = icon[..., 3:]
    icon[..., :3] = np.divide(icon[..., :3], alpha, out=np.zeros_like(icon[..., :3]), where=alpha > 0)
    return icon


def main() -> int:
    tex_dir = ROOT / 'mod/Textures/SlopWorld'
    os.makedirs(tex_dir, exist_ok=True)

    # Step 1: render the rose as its own PNG
    rose_path = os.path.join(tex_dir, 'SlopWorld_rose.png')
    render_rose_png(target_h=32, out_path=rose_path)

    # Step 2: composite
    icon = make_icon(rose_path)
    img = np.clip(icon * 255, 0, 255).astype(np.uint8)
    out_path = os.path.join(tex_dir, 'SlopWorld_icon.png')
    # The face geometry uses pawn proportions with generous surrounding space.
    # Desktop launchers display the entire PNG, so fit the composite's ink bounds
    # halfway toward filling the canvas, retaining its aspect ratio. Limiting
    # enlargement keeps the small facial details from becoming overly soft.
    artwork = Image.fromarray(img)
    bounds = artwork.getchannel('A').getbbox()
    if bounds is None:
        raise RuntimeError('application icon rendered no visible artwork')
    artwork = artwork.crop(bounds)
    margin = 4
    original_edge = max(artwork.size)
    target_edge = (original_edge + geometry.N - 2 * margin) / 2
    scale = target_edge / original_edge
    fitted = artwork.resize((round(artwork.width * scale), round(artwork.height * scale)), Image.Resampling.LANCZOS)
    canvas = Image.new('RGBA', (geometry.N, geometry.N))
    canvas.paste(fitted, ((geometry.N - fitted.width) // 2, (geometry.N - fitted.height) // 2))
    canvas.save(out_path)
    print('wrote', os.path.normpath(out_path))

    return 0


if __name__ == '__main__':
    sys.exit(main())
