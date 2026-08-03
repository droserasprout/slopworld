#!/usr/bin/env python3
"""Draws the app icon: the robot faceplate (south/head-on) with 🥀 (wilted rose,
Noto Color Emoji) growing from the top of the skull, behind the faceplate.

Two-step process:
  1. Render the rose emoji as a standalone PNG (SlopWorld_rose.png).
  2. Composite: rose behind, robot face on top, stem tip slightly intersecting
     the top of the skull for a "growing out of the head" effect.

Output: mod/Textures/SlopWorld/SlopWorld_icon.png (128x128 RGBA).

Needs numpy, pillow, pycairo and Pango (with Noto Color Emoji installed).
"""
import os
import sys

import numpy as np
from PIL import Image

# ── robot face geometry (lifted from roboface.py) ──────────────────────────

N = 128
SS = 4
S = N * SS

CX, CY, RX, RY, EXP = 64.0, 64.5, 23.5, 24.5, 3.0
HAIRLINE = 54.0
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
C_LENS = np.array([0.085, 0.095, 0.115])
C_BOLT = np.array([0.52, 0.535, 0.56])
C_GLOW = np.array([0.45, 0.75, 0.95])

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


class Face:
    def __init__(self):
        self.rgb = np.zeros((S, S, 3), np.float32)
        self.a = np.zeros((S, S), np.float32)

    def paint(self, mask, color):
        m = mask[..., None]
        self.rgb = self.rgb * (1 - m) + np.asarray(color, np.float32) * m
        self.a = np.maximum(self.a, mask)


def steel(x0, y0, x1, y1):
    t = np.clip((Y - y0) / (y1 - y0), 0, 1)[..., None]
    lit = np.clip(1.0 - (((X - (x0 + (x1 - x0) * 0.3)) ** 2
                          + (Y - (y0 + (y1 - y0) * 0.25)) ** 2) / 900.0), 0, 1)
    return np.clip(C_PLATE_TOP * (1 - t) + C_PLATE_BOT * t + lit[..., None] * 0.09, 0, 1)


def front(f, *cuts):
    region = skull(SKIN_INSET)
    for c in cuts:
        region = np.maximum(region, c)
    f.paint(cover(region), C_SEAM)
    inner = skull(SKIN_INSET)
    for c in cuts:
        inner = np.maximum(inner, c + SEAM_W)
    m = cover(inner)[..., None]
    f.rgb = f.rgb * (1 - m) + steel(CX - RX, HAIRLINE, CX + RX, CY + RY) * m
    return region


def below(y):
    return y - Y


def clipped(mask, region):
    return mask * cover(region + SEAM_W)


def eye(f, cx, cy):
    f.paint(cover(disc(cx, cy, EYE_R)), C_LENS)
    iris = cover(disc(cx, cy, IRIS_R))
    g = np.clip(1.25 - ((X - (cx - IRIS_R)) + (Y - (cy - IRIS_R))) / (IRIS_R * 4),
                0.35, 1.0)
    f.rgb = f.rgb * (1 - iris[..., None]) + C_GLOW * (g * 0.95)[..., None] * iris[..., None]
    f.a = np.maximum(f.a, iris)


def mouth(f, x0, y0, x1, y1, bars):
    f.paint(cover(rrect(x0, y0, x1, y1, MOUTH_R)), C_LENS)
    inside = cover(rrect(x0 + 0.9, y0 + 0.9, x1 - 0.9, y1 - 0.9, MOUTH_R * 0.6))
    for x in bars:
        f.paint(cover(rrect(x - BAR_W, y0, x + BAR_W, y1, BAR_W * 0.8)) * inside,
                C_PLATE_BOT)


def bolt(f, cx, cy, region):
    f.paint(clipped(cover(disc(cx, cy, BOLT_R)), region), C_BOLT)
    f.paint(clipped(cover(disc(cx, cy, BOLT_R * 0.45)), region), C_SEAM)


def draw_robot():
    f = Face()
    region = front(f, below(HAIRLINE))
    for x in EYES_X:
        eye(f, x, EYE_Y)
    mouth(f, *MOUTH, BARS_X)
    for cx, cy in BOLTS:
        bolt(f, cx, cy, region)
    px = np.dstack([f.rgb, f.a]).reshape(N, SS, N, SS, 4).mean(axis=(1, 3))
    rgb, a = px[..., :3], px[..., 3:]
    rgb = np.where(a > 1e-4, rgb / np.maximum(a, 1e-4), 0.0)
    return np.clip(np.concatenate([rgb, a], -1), 0, 1)


# ── step 1: render rose as a separate PNG ─────────────────────────────────

def render_rose_png(target_h, out_path):
    """Render 🥀 via PangoCairo, save as a standalone RGBA PNG at target_h high."""
    import gi
    gi.require_version('Pango', '1.0')
    gi.require_version('PangoCairo', '1.0')
    from gi.repository import Pango, PangoCairo
    import cairo

    # Probe at 64pt to find the right pt size for target_h
    probe_pt = 64
    surface = cairo.ImageSurface(cairo.FORMAT_ARGB32, 1024, 1024)
    ctx = cairo.Context(surface)
    layout = PangoCairo.create_layout(ctx)
    layout.set_font_description(Pango.font_description_from_string(f'Noto Color Emoji {probe_pt}'))
    layout.set_text('🥀', -1)
    ink, logical = layout.get_pixel_extents()
    ctx.move_to(-ink.x, -ink.y)
    PangoCairo.show_layout(ctx, layout)
    arr = np.frombuffer(surface.get_data(), dtype=np.uint8).reshape(1024, 1024, 4)
    arr = arr[:, :, [2, 1, 0, 3]]
    a = arr[:, :, 3]
    ys, xs = np.nonzero(a > 10)
    visible_h = ys.max() - ys.min() + 1
    pt = max(12, int(round(probe_pt * target_h / visible_h)))
    surface.finish()

    # Render at the chosen pt
    surface = cairo.ImageSurface(cairo.FORMAT_ARGB32, 1024, 1024)
    ctx = cairo.Context(surface)
    layout = PangoCairo.create_layout(ctx)
    layout.set_font_description(Pango.font_description_from_string(f'Noto Color Emoji {pt}'))
    layout.set_text('🥀', -1)
    ink, logical = layout.get_pixel_extents()
    ctx.move_to(-ink.x, -ink.y)
    PangoCairo.show_layout(ctx, layout)
    arr = np.frombuffer(surface.get_data(), dtype=np.uint8).reshape(1024, 1024, 4)
    arr = arr[:, :, [2, 1, 0, 3]]
    a = arr[:, :, 3]
    ys, xs = np.nonzero(a > 10)
    rose = arr[ys.min():ys.max() + 1, xs.min():xs.max() + 1].astype(np.float32) / 255.0

    # Scale to target_h
    rh, rw = rose.shape[:2]
    scale = target_h / rh
    new_w = max(1, int(round(rw * scale)))
    rose_pil = Image.fromarray((rose * 255).astype(np.uint8))
    rose_scaled = np.array(rose_pil.resize((new_w, target_h), Image.LANCZOS),
                           dtype=np.uint8)
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    Image.fromarray(rose_scaled).save(out_path)
    print("wrote", os.path.normpath(out_path))
    surface.finish()
    return rose_scaled, new_w, target_h


# ── step 2: composite ─────────────────────────────────────────────────────

def make_icon(rose_path):
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
    rose_left = int(round(CX - rw / 2))  # centred on the faceplate

    # Composite: rose behind, robot on top.
    icon = np.zeros((128, 128, 4), dtype=np.float32)

    # Paste the rose first.
    y0 = max(0, rose_top)
    y1 = min(128, rose_top + rh)
    x0 = max(0, rose_left)
    x1 = min(128, rose_left + rw)
    ry0 = y0 - rose_top
    rx0 = x0 - rose_left
    if y1 > y0 and x1 > x0:
        r_alpha = rose[ry0:ry0 + y1 - y0, rx0:rx0 + x1 - x0, 3]
        r_rgb = rose[ry0:ry0 + y1 - y0, rx0:rx0 + x1 - x0, :3]
        icon[y0:y1, x0:x1, :3] = r_rgb * r_alpha[..., None]
        icon[y0:y1, x0:x1, 3] = np.maximum(icon[y0:y1, x0:x1, 3], r_alpha)

    # Robot face on top, covering the rose's stem where they overlap.
    robot = draw_robot()
    ra = robot[:, :, 3]
    icon[:, :, :3] = robot[:, :, :3] * ra[..., None] \
        + icon[:, :, :3] * (1 - ra[..., None])
    icon[:, :, 3] = np.maximum(icon[:, :, 3], ra)

    return icon


def main():
    tex_dir = os.path.join(
        os.path.dirname(__file__), "..", "mod", "Textures", "SlopWorld",
    )
    os.makedirs(tex_dir, exist_ok=True)

    # Step 1: render the rose as its own PNG
    rose_path = os.path.join(tex_dir, "SlopWorld_rose.png")
    render_rose_png(target_h=32, out_path=rose_path)

    # Step 2: composite
    icon = make_icon(rose_path)
    img = np.clip(icon * 255, 0, 255).astype(np.uint8)
    out_path = os.path.join(tex_dir, "SlopWorld_icon.png")
    Image.fromarray(img).save(out_path)
    print("wrote", os.path.normpath(out_path))

    # Also update the pre-built pkg copy
    pkg_path = os.path.join(
        os.path.dirname(__file__), "..",
        "packaging", "arch", "pkg", "slopworld",
        "usr", "share", "icons", "hicolor", "128x128", "apps", "slopworld.png",
    )
    if os.path.isdir(os.path.dirname(pkg_path)):
        Image.fromarray(img).save(pkg_path)
        print("wrote", os.path.normpath(pkg_path))

    return 0


if __name__ == "__main__":
    sys.exit(main())