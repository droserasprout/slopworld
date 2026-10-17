"""Fit the shared robot-and-rose artwork to the docs favicon without desktop padding."""

import numpy as np
from PIL import Image

from tools import ROOT
from tools.assets.appicon import make_icon
from tools.assets.appicon import render_rose
from tools.assets.artwork import fit
from tools.assets.artwork import save_png


def main() -> None:
    # Render from the source composite, before the desktop icon adds margins.
    icon = make_icon(render_rose())
    artwork = Image.fromarray(np.clip(icon * 255, 0, 255).astype(np.uint8))
    canvas = fit(artwork, 128)
    target = ROOT / 'docs/theme/favicon.png'
    target.parent.mkdir(parents=True, exist_ok=True)
    save_png(canvas, target)
    print('wrote', target)


if __name__ == '__main__':
    main()
