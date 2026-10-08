"""Fit the shared robot-and-rose artwork to the docs favicon without desktop padding."""

import numpy as np
from PIL import Image

from tools import ROOT
from tools.assets.appicon import make_icon


def main() -> None:
    # Render from the source composite, before the desktop icon adds margins.
    icon = make_icon(str(ROOT / 'mod/Textures/SlopWorld/SlopWorld_rose.png'))
    artwork = Image.fromarray(np.clip(icon * 255, 0, 255).astype(np.uint8))
    bounds = artwork.getchannel('A').getbbox()
    if bounds is None:
        raise RuntimeError('favicon rendered no visible artwork')
    artwork = artwork.crop(bounds)
    size = 128
    scale = size / max(artwork.size)
    fitted = artwork.resize((round(artwork.width * scale), round(artwork.height * scale)), Image.Resampling.LANCZOS)
    canvas = Image.new('RGBA', (size, size))
    canvas.paste(fitted, ((size - fitted.width) // 2, (size - fitted.height) // 2))
    target = ROOT / 'docs/theme/favicon.png'
    target.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(target)
    print('wrote', target)


if __name__ == '__main__':
    main()
