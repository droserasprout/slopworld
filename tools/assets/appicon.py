"""Draw the application icon with a robot faceplate and a wilted rose.

The tool gets the rose from Noto Color Emoji.
It puts the rose behind the faceplate and above the skull.

Process:
  1. Render the rose in memory (also published as SlopWorld_rose.png).
  2. Put the faceplate over the rose.
  3. Move the stem tip slightly into the top of the skull.

Usage: just generate-appicon

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
from tools.assets.artwork import crop_ink
from tools.assets.artwork import fit
from tools.assets.artwork import save_png

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
    px = cast(
        'geometry.FloatArray',
        np.dstack([f.rgb, f.a]).reshape(geometry.N, geometry.SS, geometry.N, geometry.SS, 4).mean(axis=(1, 3)),
    )
    rgb, a = px[..., :3], px[..., 3:]
    rgb = np.where(a > 1e-4, rgb / np.maximum(a, 1e-4), 0.0)
    return cast('geometry.FloatArray', np.clip(np.concatenate([rgb, a], -1), 0, 1))


def render_rose(target_h: int = 32) -> Image.Image:
    """Render the shared rose layer without using generated files as inputs."""
    from tools.assets.emoji_render import render

    rose = crop_ink(render('🥀'), threshold=10)
    width = max(1, round(rose.width * target_h / rose.height))
    return rose.resize((width, target_h), Image.Resampling.LANCZOS)


def make_icon(rose_image: Image.Image) -> NDArray[np.float32]:
    """Composite straight-alpha rose artwork below the robot face."""
    rose = np.array(rose_image, dtype=np.float32) / 255.0
    rh, rw = rose.shape[:2]

    # The rose grows from the top of the skull, so it sits higher than the
    # face. The stem tip (bottom row of the rose image) pokes into the top of
    # the faceplate at the hairline, making it read as growing *out of* the
    # head.
    stem_tip_y = 56  # hairline + 2, rose scaled down so needs lowering
    rose_top = stem_tip_y - rh + 1  # stem_tip_y = rose_top + rh - 1
    rose_left = round(geometry.CX - rw / 2)  # centred on the faceplate

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
    tex_dir.mkdir(parents=True, exist_ok=True)

    # Render the shared layer in memory; the PNG is an output only.
    rose_path = tex_dir / 'SlopWorld_rose.png'
    rose = render_rose()

    # Step 2: composite
    icon = make_icon(rose)
    img = np.clip(icon * 255, 0, 255).astype(np.uint8)
    out_path = tex_dir / 'SlopWorld_icon.png'
    # The face geometry uses pawn proportions with generous surrounding space.
    # Desktop launchers display the entire PNG, so fit the composite's ink bounds
    # halfway toward filling the canvas, retaining its aspect ratio. Limiting
    # enlargement keeps the small facial details from becoming overly soft.
    artwork = Image.fromarray(img)
    ink = crop_ink(artwork)
    original_edge = max(ink.size)
    target_edge = (original_edge + geometry.N - 8) / 2
    canvas = fit(ink, geometry.N, breathe=geometry.N / target_edge)
    # Publish outputs only after the composition has succeeded.
    save_png(canvas, out_path)
    save_png(rose, rose_path)
    print('wrote', os.path.normpath(out_path))

    return 0


if __name__ == '__main__':
    sys.exit(main())
