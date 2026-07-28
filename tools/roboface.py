#!/usr/bin/env python3
"""Draws mod/Textures/SlopWorld/RobotFace_{south,east}.png.

The faceplate is geometry, not art, so it lives here as code: run
`python3 tools/roboface.py` after changing a constant. Needs numpy and pillow.

It is an overlay, not a head. Every constant below is in the *head's* frame - a
128x128 texture whose skull is a ~47px blob centred at (64, 64.5) - and the
metal is clipped to that skull rather than drawn as a shape of its own. An
earlier cut drew a rounded square inset from the head and it read as a mask held
up to the face: it carried its own closed outline, it was framed by an even rim
of skin, and its corners pushed into a round silhouette. Here the metal runs out
to the head's own edge and the only dark line is the seam along the hairline.

No north texture: a faceplate has no back, and the node's visibleFacing leaves
the pawn's own head showing when it turns away. West is Graphic_Multi's mirror.
"""
import os

import numpy as np
from PIL import Image

OUT = os.path.join(os.path.dirname(__file__), "..", "mod", "Textures", "SlopWorld")

N = 128                     # the head frame RimWorld draws us into
SS = 4                      # supersample factor, box-filtered down at the end
S = N * SS

# The shape the metal is cut against: a superellipse centred at (CX, CY),
# spanning x 40.5..87.5 and y 40..89. The exponent matters as much as the radii.
CX, CY, RX, RY, EXP = 64.0, 64.5, 23.5, 24.5, 3.0

# HAIRLINE is the cut across the brow; SKIN_INSET is how far short of the head's
# outline the metal runs out at the sides and jaw, that outline being part of the
# head's silhouette and so the head's to own.
HAIRLINE = 54.0
SKIN_INSET = 2.0
SEAM_W = 1.6

# In profile the plate wraps the front of the head rather than all of it. Behind
# this seam the pawn's own head shows, which is what makes turning from east to
# north continuous.
BACK_X = 56.0

# Big enough to nearly span the plate and far enough apart to survive the
# downsample as two things. Sockets are cut dark, the lens sits inside them.
EYE_Y, EYE_R, IRIS_R = 64.0, 5.8, 3.5
EYES_X = (57.0, 71.0)

# Mouth: a straight horizontal round-cornered recess with vertical grill bars.
MOUTH = (53.5, 74.0, 74.5, 80.5)
MOUTH_R = 2.8
BARS_X = (56.5, 60.5, 64.5, 68.5, 72.5)
BAR_W = 0.8

BOLTS = ((52.0, 58.0), (76.0, 58.0), (52.5, 84.0), (75.5, 84.0))
BOLT_R = 1.2

# The only dark line the plate draws is the seam along its cuts; the silhouette
# is the head's.
C_PLATE_TOP = np.array([0.42, 0.435, 0.46])
C_PLATE_BOT = np.array([0.25, 0.265, 0.29])
C_SEAM = np.array([0.16, 0.175, 0.195])
C_LENS = np.array([0.085, 0.095, 0.115])
C_BOLT = np.array([0.52, 0.535, 0.56])
C_GLOW = np.array([0.45, 0.75, 0.95])   # the "working" blue, straight from the UI

_y, _x = np.mgrid[0:S, 0:S]
X, Y = (_x + 0.5) / SS, (_y + 0.5) / SS


def cover(sd):
    """Signed distance in pixels (negative inside) -> coverage of a sample."""
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
    """The head's own outline as a signed distance, optionally pulled inwards."""
    dx, dy = (X - CX) / RX, (Y - CY) / RY
    f = np.abs(dx) ** EXP + np.abs(dy) ** EXP
    # Crude but stable distance: scale the implicit value back into pixels.
    return (f ** (1.0 / EXP) - 1.0) * min(RX, RY) + inset


class Face:
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
        path = os.path.join(OUT, f"RobotFace_{name}.png")
        Image.fromarray((img * 255).astype(np.uint8)).save(path)
        print("wrote", os.path.normpath(path))


def steel(x0, y0, x1, y1):
    """Brushed plate: a vertical gradient with a lit patch up and left."""
    t = np.clip((Y - y0) / (y1 - y0), 0, 1)[..., None]
    lit = np.clip(1.0 - (((X - (x0 + (x1 - x0) * 0.3)) ** 2
                          + (Y - (y0 + (y1 - y0) * 0.25)) ** 2) / 900.0), 0, 1)
    return np.clip(C_PLATE_TOP * (1 - t) + C_PLATE_BOT * t + lit[..., None] * 0.09, 0, 1)


def front(f, *cuts):
    """The metal everything else is cut into: the head, minus everything above or
    behind a cut. A cut is a signed distance of its own, so `max` of them is the
    intersection - and each one gets the seam, while the sides the skull clipped
    get nothing, because that edge belongs to the head.

    Returns the region, for clipping details to it."""
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
    """A cut keeping everything below y - the hairline."""
    return y - Y


def ahead_of(x):
    """A cut keeping everything right of x - the seam down the side, in profile."""
    return x - X


def clipped(mask, region):
    """Keeps a detail off the skin, whatever the cuts did to the metal."""
    return mask * cover(region + SEAM_W)


def eye(f, cx, cy):
    """A dark socket with a lit lens in it. The lens is brightest up and to the
    left, so both eyes read as catching the same light as the plate."""
    f.paint(cover(disc(cx, cy, EYE_R)), C_LENS)
    iris = cover(disc(cx, cy, IRIS_R))
    g = np.clip(1.25 - ((X - (cx - IRIS_R)) + (Y - (cy - IRIS_R))) / (IRIS_R * 4),
                0.35, 1.0)
    f.rgb = f.rgb * (1 - iris[..., None]) + C_GLOW * (g * 0.95)[..., None] * iris[..., None]
    f.a = np.maximum(f.a, iris)


def mouth(f, x0, y0, x1, y1, bars):
    """Straight, horizontal, round-cornered, and vented. The recess is the dark
    thing and the bars are the metal left standing in it, which is the way round
    a grill reads; inverted, it becomes a row of lit teeth. Bars are clipped to
    the recess so none of them sits on the rim."""
    f.paint(cover(rrect(x0, y0, x1, y1, MOUTH_R)), C_LENS)
    inside = cover(rrect(x0 + 0.9, y0 + 0.9, x1 - 0.9, y1 - 0.9, MOUTH_R * 0.6))
    for x in bars:
        f.paint(cover(rrect(x - BAR_W, y0, x + BAR_W, y1, BAR_W * 0.8)) * inside,
                C_PLATE_BOT)


def bolt(f, cx, cy, region):
    """Clipped to the metal, because a bolt that spills onto the skin is the mask
    read coming back in miniature."""
    f.paint(clipped(cover(disc(cx, cy, BOLT_R)), region), C_BOLT)
    f.paint(clipped(cover(disc(cx, cy, BOLT_R * 0.45)), region), C_SEAM)


def south():
    """Head on: metal from the hairline down, both eyes, the grill, four bolts."""
    f = Face()
    region = front(f, below(HAIRLINE))
    for x in EYES_X:
        eye(f, x, EYE_Y)
    mouth(f, *MOUTH, BARS_X)
    for cx, cy in BOLTS:
        bolt(f, cx, cy, region)
    return f


def east():
    """In profile the metal wraps the front of the head, so there is a second cut
    down the side and one eye. The bolts go on that seam, which is the only part
    of the plate's own edge still facing us."""
    f = Face()
    region = front(f, below(HAIRLINE), ahead_of(BACK_X))
    eye(f, 73.5, EYE_Y)
    mouth(f, 63.0, MOUTH[1], 80.0, MOUTH[3], (66.0, 69.5, 73.0, 76.5))
    for cy in (59.5, 84.0):
        bolt(f, BACK_X + 4.0, cy, region)
    return f


if __name__ == "__main__":
    for name, draw in (("south", south), ("east", east)):
        draw().save(name)
