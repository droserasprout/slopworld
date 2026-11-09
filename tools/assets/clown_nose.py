"""Draw the geometric clown-nose overlay in the robot faceplate coordinate system."""

from PIL import Image
from PIL import ImageDraw

from tools import ROOT


def circle(draw: ImageDraw.ImageDraw, cx: float, cy: float, radius: float, color: str, scale: int) -> None:
    draw.ellipse(
        ((cx - radius) * scale, (cy - radius) * scale, (cx + radius) * scale, (cy + radius) * scale),
        fill=color,
    )


def main() -> None:
    scale = 4
    output = ROOT / 'mod/Textures/SlopWorld'
    output.mkdir(parents=True, exist_ok=True)
    # East is mirrored by Graphic_Multi for west; north has no visible nose.
    for facing, x in (('south', 64), ('east', 84)):
        image = Image.new('RGBA', (128 * scale, 128 * scale))
        draw = ImageDraw.Draw(image)

        circle(draw, x, 70.5, 4.5, '#4e2b30', scale)
        circle(draw, x, 70, 4, '#813e47', scale)
        circle(draw, x - 1, 69, 1, '#99565e', scale)
        image.resize((128, 128), Image.Resampling.LANCZOS).save(output / f'ClownNose_{facing}.png')


if __name__ == '__main__':
    main()
